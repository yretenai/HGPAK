using System.Buffers;

namespace HelloPak;

public interface IHelloPakBuffer : IDisposable {
	public static NullBuffer Empty { get; } = new();

	public int Length { get; }
	public ReadOnlySpan<byte> Data { get; }
	public byte this[int offset] { get; }
}

public sealed class NullBuffer : IHelloPakBuffer {
	public int Length => 0;
	public ReadOnlySpan<byte> Data => ReadOnlySpan<byte>.Empty;
	public byte this[int offset] => 0;

	public void Dispose() { }
}

public sealed class HelloPakMemoryBuffer(IMemoryOwner<byte> Buffer, int Size) : IHelloPakBuffer {
	public Span<byte> WritableData => MemoryData.Span;
	public Memory<byte> MemoryData => Buffer.Memory[..Size];
	public int Length => Size;
	public ReadOnlySpan<byte> Data => WritableData;
	public byte this[int offset] => Data[offset];

	public void Dispose() {
		Buffer.Dispose();
	}
}
