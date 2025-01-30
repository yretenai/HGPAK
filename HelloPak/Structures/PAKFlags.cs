namespace HelloPak.Structures;

[Flags]
public enum PAKFlags : ulong {
	CaseInsensitivePaths = 1 << 0,
}

public enum PAKCompression {
	ZStandard,
	Oodle,
	LZ4,

	MacOS = LZ4,
	Windows = ZStandard,
}

public static class PAKFlagsExtensions {
	public static bool HasFlagFast(this PAKFlags value, PAKFlags ArchiveFlags) => (value & ArchiveFlags) != 0;
}
