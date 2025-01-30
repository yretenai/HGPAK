using System.Runtime.InteropServices;

namespace HelloPak.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 0xC)]
public record struct PAKFileHeader {
	public long FileCount { get; set; }
	public long BlockCount { get; set; }
}
