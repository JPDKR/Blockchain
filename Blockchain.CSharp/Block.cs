namespace Blockchain.CSharp;

/// <summary>
/// Bloque inmutable de la cadena. Se crea a través de <see cref="BlockChain.Add"/>
/// o se rehidrata desde un almacenamiento con el constructor público; en ese caso la
/// integridad se comprueba con <see cref="BlockChain.Validate()"/>.
/// </summary>
public sealed class Block
{
    private readonly byte[] _data;
    private readonly byte[] _prevHash;
    private readonly byte[] _hash;

    public Block(long index, DateTimeOffset timestamp, byte[] data, byte[] prevHash, long nonce, byte[] hash)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(prevHash);
        ArgumentNullException.ThrowIfNull(hash);

        Index = index;
        TimeStamp = timestamp.ToUniversalTime();
        Nonce = nonce;
        // Copias defensivas: nadie puede alterar el bloque desde afuera.
        _data = (byte[])data.Clone();
        _prevHash = (byte[])prevHash.Clone();
        _hash = (byte[])hash.Clone();
    }

    /// <summary>Posición en la cadena (el génesis es 0).</summary>
    public long Index { get; }

    /// <summary>Momento de creación, siempre en UTC.</summary>
    public DateTimeOffset TimeStamp { get; }

    public long Nonce { get; }

    public ReadOnlyMemory<byte> Data => _data;
    public ReadOnlyMemory<byte> PrevHash => _prevHash;
    public ReadOnlyMemory<byte> Hash => _hash;

    public string HashHex => Convert.ToHexString(_hash);
    public string PrevHashHex => Convert.ToHexString(_prevHash);

    public override string ToString() =>
        $"#{Index} {TimeStamp:O} nonce={Nonce}\n  hash: {HashHex}\n  prev: {PrevHashHex}";
}
