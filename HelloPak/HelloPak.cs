using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using HelloPak.Structures;
using Waterfall.Compression;
using LZMADecoder = SevenZip.Compression.LZMA.Decoder;

namespace HelloPak;

public sealed class HelloPak : IDisposable {
	internal const int BlockSize = 0x10000;
	private static readonly char[] LineSeparators = ['\n', (char) 0];

	public HelloPak(Stream stream) {
		BaseStream = stream;

		if (stream.Length <= 0x30) {
			FileEntries = [];
			Manifest = [];
			BlockSizeBuffer = MemoryPool<long>.Shared.Rent(0);
			BlockOffsetBuffer = MemoryPool<long>.Shared.Rent(0);
			return;
		}

		Span<PAKHeader> header = stackalloc PAKHeader[1];
		BaseStream.ReadExactly(MemoryMarshal.AsBytes(header));
		Header = header[0];

		Manifest = new Dictionary<string, PAKHash>(Header.ArchiveFlags.HasFlagFast(PAKFlags.CaseInsensitivePaths) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

		// check if the file starts with "HGPAK"
		if (Header.Magic != PAKHeader.HGPAK) {
			throw new InvalidDataException("Not a HGPAK File");
		}

		// we only support version 2
		if (Header.Version != 2) {
			throw new NotSupportedException("PAK has an unsupported version");
		}

		// read entries in one go
		var entryCount = int.CreateChecked(Header.FAT.FileCount);
		var entries = MemoryPool<PAKFileEntry>.Shared.Rent(entryCount);
		var entriesSpan = entries.Memory.Span[..entryCount];
		var entriesBytes = MemoryMarshal.AsBytes(entriesSpan);
		BaseStream.ReadExactly(entriesBytes);

		// amortize it into a dictionary.
		FileEntries = new Dictionary<PAKHash, PAKFileEntry>(entryCount);
		foreach (var entry in entriesSpan) {
			FileEntries[entry.Hash] = entry;
		}

		// read the compression block list in one go
		var blockCount = int.CreateChecked(Header.FAT.BlockCount);
		BlockSizeBuffer = MemoryPool<long>.Shared.Rent(blockCount);
		BlockOffsetBuffer = MemoryPool<long>.Shared.Rent(blockCount);
		var blockSizeSpan = BlockSizeBuffer.Memory.Span[..blockCount];
		var blockOffsetSpan = BlockOffsetBuffer.Memory.Span[..blockCount];
		BaseStream.ReadExactly(MemoryMarshal.AsBytes(blockSizeSpan));
		var offset = Header.Base;
		for (var index = 0; index < blockCount; index++) {
			blockOffsetSpan[index] = offset;
			offset += blockSizeSpan[index];
			offset = unchecked(offset + 15) & ~15L;
		}

		// read manifest (if it exists)
		// manifest has no hash.
		using var manifest = OpenFile(entriesSpan[0].Hash);
		if (manifest.Length > 0 && manifest.Data[0] != 0) {
			// todo: check if this always matches the file order, it might be possible to just skip md5-ing the path.
			foreach (var filePath in Encoding.ASCII.GetString(manifest.Data).Split(LineSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
				// normalize paths.
				// todo: can relative paths start with '/'?
				Manifest[filePath.Replace('\\', '/')] = new PAKHash(Header.ArchiveFlags.HasFlagFast(PAKFlags.CaseInsensitivePaths) ? filePath.ToLowerInvariant() : filePath);
			}
		}
	}

	// allocate 255 bytes for random slop to avoid making allocations over and over and over again.
	internal byte[] ScratchPad { get; } = ArrayPool<byte>.Shared.Rent(byte.MaxValue);
	internal PAKHeader Header { get; set; }
	internal IMemoryOwner<long> BlockSizeBuffer { get; set; }
	internal IMemoryOwner<long> BlockOffsetBuffer { get; set; }

	public Stream BaseStream { get; set; }
	public Dictionary<PAKHash, PAKFileEntry> FileEntries { get; set; }
	public Dictionary<string, PAKHash> Manifest { get; set; }
	public IEnumerable<string> Paths => Manifest.Keys;

	public void Dispose() {
		ArrayPool<byte>.Shared.Return(ScratchPad);
		BaseStream.Dispose();
		BlockSizeBuffer.Dispose();
	}

	internal IHelloPakBuffer OpenFile(PAKFileEntry file) {
		// unfortunately, Span<T> only holds 2 GB.
		// it's not impossible to refactor this support larger (i.e. a PAKMemoryStream implementation of IPAKBuffer)
		if (file.Size > int.MaxValue) {
			throw new NotSupportedException("Large files are not supported.");
		}

		// rent some data from the memory pool for reading and decompressing.
		using var rentedBlockBuffer = MemoryPool<byte>.Shared.Rent(BlockSize);
		using var rentedDataBuffer = MemoryPool<byte>.Shared.Rent(BlockSize);
		var blockBuffer = rentedBlockBuffer.Memory.Span[..BlockSize];
		var offsetInStream = (int) (file.Offset - Header.Base);
		var blockIndex = offsetInStream / BlockSize;
		var resultBuffer = new HelloPakMemoryBuffer(MemoryPool<byte>.Shared.Rent((int) file.Size), (int) file.Size);
		var resultSpan = resultBuffer.WritableData;
		var blocks = BlockSizeBuffer.Memory.Span;
		var blockOffsets = BlockOffsetBuffer.Memory.Span;

		var fill = (int) file.Size;
		var offset = offsetInStream % BlockSize;
		while (fill > 0) {
			// read the next block
			BaseStream.Position = blockOffsets[blockIndex];
			var blockSize = (int) blocks[blockIndex++];
			var localBlockSlice = blockSize == 0 ? BlockSize : blockSize;
			var blockSlice = blockBuffer[..localBlockSlice];
			BaseStream.ReadExactly(blockSlice);

			var compressionType = CompressionType.LZ4;
			if (IsOodle(blockSlice)) {
				compressionType = CompressionType.Oodle;
			} else if (IsZStandard(blockSlice)) {
				compressionType = CompressionType.Zstd;
			}

			var n = CompressionHelper.Decompress(compressionType, rentedBlockBuffer.Memory[..localBlockSlice], rentedDataBuffer.Memory[..BlockSize]);

			if (n <= 0) {
				throw new InvalidOperationException();
			}

			var blockShift = rentedDataBuffer.Memory.Span.Slice(offset, n - offset);
			offset = 0;
			if (blockShift.Length > fill) {
				blockShift = blockShift[..fill];
			}

			blockShift.CopyTo(resultSpan[(resultBuffer.Length - fill)..]);
			fill -= n;
		}

		return resultBuffer;
	}

	private static bool IsOodle(Span<byte> span) => (span[0] & 0x3F) == 0xC;
	private static bool IsZStandard(Span<byte> span) => span[1] == 0xb5 && span[2] == 0x2f && span[3] == 0xfd;

	public IHelloPakBuffer OpenFile(PAKHash hash) => FileEntries.TryGetValue(hash, out var file) ? OpenFile(file) : IHelloPakBuffer.Empty;
	public IHelloPakBuffer OpenFile(string path) => Manifest.TryGetValue(path.Replace('\\', '/'), out var hash) ? OpenFile(hash) : OpenFile(new PAKHash(Header.ArchiveFlags.HasFlagFast(PAKFlags.CaseInsensitivePaths) ? path.ToUpperInvariant() : path));

	public Dictionary<PAKHash, string> BuildReversePaths() => Manifest.ToDictionary(x => x.Value, x => x.Key);
}
