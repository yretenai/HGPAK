using System.Runtime.InteropServices;

namespace HelloPak.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 0x20)]
public record struct PAKHeader {
	public const ulong HGPAK = 0x4B41504748;

	public ulong Magic { get; set; }
	public ulong Version { get; set; }
	public PAKFileHeader FAT { get; set; }
	public PAKFlags ArchiveFlags { get; set; }
	public long Base { get; set; }
}
