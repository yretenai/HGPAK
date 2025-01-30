using System.Runtime.InteropServices;

namespace HelloPak.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 0x1E)]
public record struct PAKFileEntry {
	public PAKHash Hash { get; set; }
	public long Offset { get; set; }
	public long Size { get; set; }
}
