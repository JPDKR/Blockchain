using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Blockchain.CSharp;

/// <summary>
/// Cálculo canónico del hash: independiente de la cultura, del huso horario y de la plataforma.
/// Formato (little-endian): versión(1) | índice(8) | ticks UTC(8) | nonce(8) |
/// largo prevHash(4) | prevHash | largo datos(4) | datos.
/// </summary>
public static class BlockHasher
{
    private const byte FormatVersion = 1;

    public static int HashLength(BlockHashAlgorithm algorithm) => algorithm switch
    {
        BlockHashAlgorithm.Sha256 => SHA256.HashSizeInBytes,
        BlockHashAlgorithm.Sha384 => SHA384.HashSizeInBytes,
        BlockHashAlgorithm.Sha512 => SHA512.HashSizeInBytes,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
    };

    public static byte[] ComputeHash(
        BlockHashAlgorithm algorithm, long index, DateTimeOffset timestamp, long nonce,
        ReadOnlySpan<byte> prevHash, ReadOnlySpan<byte> data)
    {
        var buffer = new byte[1 + 8 + 8 + 8 + 4 + prevHash.Length + 4 + data.Length];
        WriteHeader(buffer, index, timestamp, nonce, prevHash, data);
        return Hash(algorithm, buffer);
    }

    public static byte[] ComputeHash(BlockHashAlgorithm algorithm, Block block) =>
        ComputeHash(algorithm, block.Index, block.TimeStamp, block.Nonce, block.PrevHash.Span, block.Data.Span);

    /// <summary>Busca un nonce cuyo hash cumpla la dificultad. Devuelve (nonce, hash).</summary>
    internal static (long Nonce, byte[] Hash) Mine(
        BlockHashAlgorithm algorithm, int difficultyBits, long index, DateTimeOffset timestamp,
        ReadOnlySpan<byte> prevHash, ReadOnlySpan<byte> data, CancellationToken cancellationToken)
    {
        var buffer = new byte[1 + 8 + 8 + 8 + 4 + prevHash.Length + 4 + data.Length];
        WriteHeader(buffer, index, timestamp, 0, prevHash, data);
        var nonceSlot = buffer.AsSpan(1 + 8 + 8, 8);

        for (long nonce = 0; ; nonce++)
        {
            if ((nonce & 0xFFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            BinaryPrimitives.WriteInt64LittleEndian(nonceSlot, nonce);
            var hash = Hash(algorithm, buffer);
            if (MeetsDifficulty(hash, difficultyBits))
                return (nonce, hash);
        }
    }

    public static bool MeetsDifficulty(ReadOnlySpan<byte> hash, int difficultyBits)
    {
        int fullBytes = difficultyBits / 8, remainingBits = difficultyBits % 8;
        if (hash.Length * 8 < difficultyBits)
            return false;
        for (int i = 0; i < fullBytes; i++)
            if (hash[i] != 0)
                return false;
        return remainingBits == 0 || (hash[fullBytes] >> (8 - remainingBits)) == 0;
    }

    private static void WriteHeader(Span<byte> buffer, long index, DateTimeOffset timestamp, long nonce,
        ReadOnlySpan<byte> prevHash, ReadOnlySpan<byte> data)
    {
        int p = 0;
        buffer[p++] = FormatVersion;
        BinaryPrimitives.WriteInt64LittleEndian(buffer[p..], index); p += 8;
        BinaryPrimitives.WriteInt64LittleEndian(buffer[p..], timestamp.UtcTicks); p += 8;
        BinaryPrimitives.WriteInt64LittleEndian(buffer[p..], nonce); p += 8;
        BinaryPrimitives.WriteInt32LittleEndian(buffer[p..], prevHash.Length); p += 4;
        prevHash.CopyTo(buffer[p..]); p += prevHash.Length;
        BinaryPrimitives.WriteInt32LittleEndian(buffer[p..], data.Length); p += 4;
        data.CopyTo(buffer[p..]);
    }

    private static byte[] Hash(BlockHashAlgorithm algorithm, ReadOnlySpan<byte> buffer) => algorithm switch
    {
        BlockHashAlgorithm.Sha256 => SHA256.HashData(buffer),
        BlockHashAlgorithm.Sha384 => SHA384.HashData(buffer),
        BlockHashAlgorithm.Sha512 => SHA512.HashData(buffer),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
    };
}
