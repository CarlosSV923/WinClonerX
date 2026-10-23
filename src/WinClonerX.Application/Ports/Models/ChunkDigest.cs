namespace WinClonerX.Application.Ports.Models;

using System.Buffers.Binary;
using System.Security.Cryptography;

/// <summary>Digest SHA-256 compacto almacenado en cuatro palabras de 64 bits.</summary>
public readonly record struct Sha256Digest(ulong Word0, ulong Word1, ulong Word2, ulong Word3)
{
    public static Sha256Digest FromBytes(ReadOnlySpan<byte> hash)
    {
        if (hash.Length != 32)
        {
            throw new ArgumentException("A SHA-256 digest must contain 32 bytes.", nameof(hash));
        }

        return new Sha256Digest(
            BinaryPrimitives.ReadUInt64LittleEndian(hash),
            BinaryPrimitives.ReadUInt64LittleEndian(hash[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(hash[16..]),
            BinaryPrimitives.ReadUInt64LittleEndian(hash[24..]));
    }

    public bool Matches(ReadOnlySpan<byte> hash)
    {
        if (hash.Length != 32)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian(expected, Word0);
        BinaryPrimitives.WriteUInt64LittleEndian(expected[8..], Word1);
        BinaryPrimitives.WriteUInt64LittleEndian(expected[16..], Word2);
        BinaryPrimitives.WriteUInt64LittleEndian(expected[24..], Word3);
        return CryptographicOperations.FixedTimeEquals(expected, hash);
    }
}

/// <summary>Digest SHA-256 de un chunk escrito en el disco de destino.</summary>
public readonly record struct ChunkDigest(long TargetOffset, int Length, Sha256Digest Hash);
