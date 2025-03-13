using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using HelloPak.Structures;
using Waterfall.Compression;

namespace HelloPak;

public sealed class HelloPakBuilder : IDisposable {
	public HelloPakBuilder(HelloPak? archive) {
		Archive = archive;

		if (Archive == null) {
			return;
		}

		var reverse = Archive.BuildReversePaths();
		foreach (var (hash, entry) in Archive.FileEntries) {
			var buffer = Archive.OpenFile(entry);
			if (buffer is not HelloPakMemoryBuffer memoryBuffer) {
				continue;
			}

			Files.Add(new PAKTempFile(hash, reverse.GetValueOrDefault(hash), memoryBuffer));
		}
	}

	private HelloPak? Archive { get; }
	private List<PAKTempFile> Files { get; } = [];

	public void Dispose() {
		Archive?.Dispose();
		foreach (var file in Files) {
			file.Dispose();
		}
	}

	public void DeleteFile(string path) => DeleteFile(new PAKHash(path));

	public void DeleteFile(PAKHash hash) {
		var toRemove = Files.Where(x => x.Hash == hash).ToArray();
		foreach (var removed in toRemove) {
			Files.Remove(removed);
			removed.Dispose();
		}
	}

	public void AddFile(string path, HelloPakMemoryBuffer buffer) {
		if (Archive?.Header.ArchiveFlags.HasFlagFast(PAKFlags.CaseInsensitivePaths) == true) {
			path = path.ToLowerInvariant();
		}

		path = path.Replace('\\', '/');
		AddFile(new PAKHash(path), path, buffer);
	}

	public void AddFile(PAKHash hash, string? path, HelloPakMemoryBuffer buffer) {
		if (string.IsNullOrEmpty(path)) {
			var existing = Files.FirstOrDefault(x => x.Hash == hash);
			if (existing != null) {
				path = existing.Path;
			}
		}

		DeleteFile(hash);
		Files.Add(new PAKTempFile(hash, path, buffer));
	}

	public void Build(Stream output, string name, PAKCompression compressionType = PAKCompression.Windows, PAKFlags flags = PAKFlags.CaseInsensitivePaths) {
		var manifest = new StringBuilder();
		foreach (var file in Files.Where(x => x.Path != null)) {
			manifest.Append(file.Path!);
			manifest.Append("\r\n");
		}

		var manifestBytes = Encoding.ASCII.GetBytes(manifest.ToString().TrimEnd('\n', '\r')).AsMemory();

		using var compressedStream = new MemoryStream();
		using var blockBuffer = new MemoryStream();
		var fileRecords = new List<PAKFileEntry>();
		var blockIndex = 0;

		fileRecords.Add(new PAKFileEntry {
			Hash = new PAKHash(name), // TODO: figure out what the manifest filename is.
			Offset = compressedStream.Length,
			Size = manifestBytes.Length,
		});

		CompressFile(manifestBytes, blockBuffer, compressedStream, compressionType, ref blockIndex);

		foreach (var file in Files.Skip(1).OrderBy(x => x.Path != null)) {
			fileRecords.Add(new PAKFileEntry {
				Hash = file.Hash,
				Offset = compressedStream.Length,
				Size = file.Buffer.Length,
			});
			CompressFile(file.Buffer.MemoryData, blockBuffer, compressedStream, compressionType, ref blockIndex);
		}

		var startOffset = (int) (Unsafe.SizeOf<PAKHeader>() + fileRecords.Count * Unsafe.SizeOf<PAKFileEntry>() + blockBuffer.Length);

		Span<PAKHeader> newHeader = stackalloc PAKHeader[1];
		newHeader[0] = new PAKHeader {
			Magic = PAKHeader.HGPAK,
			Version = 2,
			FAT = new PAKFileHeader {
				FileCount = fileRecords.Count,
				BlockCount = blockIndex,
			},
			Base = startOffset,
			ArchiveFlags = flags & PAKFlags.CaseInsensitivePaths, // we don't support any other flags yet
		};
		output.Write(MemoryMarshal.AsBytes(newHeader));
		Span<PAKFileEntry> adjusted = stackalloc PAKFileEntry[1];
		foreach (var entry in fileRecords) {
			adjusted[0] = entry with {
				Offset = startOffset + entry.Offset,
			};
			output.Write(MemoryMarshal.AsBytes(adjusted));
		}

		blockBuffer.Flush();
		blockBuffer.Position = 0;
		blockBuffer.CopyTo(output);
		compressedStream.Position = 0;
		compressedStream.CopyTo(output);
	}

	private static void CompressFile(Memory<byte> data, MemoryStream blockBuffer, MemoryStream compressedStream, PAKCompression compressionType, ref int blockIndex) {
		Span<long> lengthBuf = stackalloc long[1];

		var blockSize = compressionType switch {
			                PAKCompression.ZStandard => HelloPak.BlockSizeZSTD,
			                PAKCompression.Oodle => HelloPak.BlockSizeOodle,
			                PAKCompression.LZ4 => HelloPak.BlockSizeLZ,
			                _ => throw new ArgumentOutOfRangeException(nameof(compressionType), compressionType, null)
		                };
		using var compressedBlock = MemoryPool<byte>.Shared.Rent(blockSize);
		var compressedMemory = compressedBlock.Memory[..blockSize];
		var compressedSpan = compressedMemory.Span;

		for (var i = 0; i < data.Length; i += blockSize) {
			blockIndex++;

			var slice = data[i..];
			if (slice.Length > blockSize) {
				slice = slice[..blockSize];
			}

			var start = compressedStream.Length;
			start = unchecked(start + 15) & ~15L;
			compressedStream.Position = start;

			var targetType = compressionType switch {
				                 PAKCompression.ZStandard => CompressionType.Zstd,
				                 PAKCompression.Oodle => CompressionType.Oodle,
				                 PAKCompression.LZ4 => CompressionType.LZ4,
				                 _ => throw new ArgumentOutOfRangeException(nameof(compressionType), compressionType, null),
			                 };

			var n = CompressionHelper.Compress(targetType, compressedMemory, slice);
			if (n <= 0) {
				throw new InvalidOperationException();
			}

			compressedStream.Write(compressedSpan[..n]);

			lengthBuf[0] = compressedStream.Length - start;
			blockBuffer.Write(MemoryMarshal.AsBytes(lengthBuf));
		}
	}

	private sealed record PAKTempFile(PAKHash Hash, string? Path, HelloPakMemoryBuffer Buffer) : IDisposable {
		public void Dispose() => Buffer.Dispose();
	}
}
