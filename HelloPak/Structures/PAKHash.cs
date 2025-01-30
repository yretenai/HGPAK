using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace HelloPak.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 1), InlineArray(0x10)]
public struct PAKHash() : IEquatable<PAKHash>, IEqualityOperators<PAKHash, PAKHash, bool> {
	private byte Value = default;

	public PAKHash(ReadOnlySpan<byte> data) : this() => MD5.HashData(data, this);

	public PAKHash(string name) : this() {
		if (name.Length > 0xFFF) {
			throw new ArgumentOutOfRangeException(nameof(name), "Name is too long");
		}

		Span<byte> bytes = stackalloc byte[Encoding.ASCII.GetByteCount(name)];
		var count = Encoding.ASCII.GetBytes(name, bytes);
		MD5.HashData(bytes[..count], this);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public override string ToString() {
		Span<char> buffer = stackalloc char[0x20];
		for (var i = 0; i < 0x10; ++i) {
			var lo = this[i] & 0xF;
			var hi = (this[i] >> 4) & 0xF;
			buffer[i << 1] = (char) (hi > 9 ? 'a' + (hi - 10) : '0' + hi);
			buffer[(i << 1) + 1] = (char) (lo > 9 ? 'a' + (lo - 10) : '0' + lo);
		}

		return new string(buffer);
	}

	public static implicit operator PAKHash(ReadOnlySpan<byte> hash) => MemoryMarshal.Read<PAKHash>(hash);
	public bool Equals(PAKHash other) => this == other;
	public override bool Equals(object? obj) => obj is PAKHash other && Equals(other);
	public static bool operator ==(PAKHash left, PAKHash right) => ((Span<byte>) left).SequenceEqual(right);
	public static bool operator !=(PAKHash left, PAKHash right) => !(left == right);

	public override int GetHashCode() {
		var self = MemoryMarshal.Cast<byte, uint>(this);
		return HashCode.Combine(self[0], self[1], self[2], self[3]);
	}
}
