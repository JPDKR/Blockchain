namespace Blockchain.CSharp;

/// <summary>
/// Almacenamiento de bloques, sólo de agregado. Cada proyecto puede implementar el suyo
/// (tabla SQL, archivo, blob, etc.).
/// </summary>
/// <remarks>
/// Contrato de <see cref="Append"/>: debe rechazar con <see cref="BlockStoreConcurrencyException"/>
/// un bloque cuyo <see cref="Block.Index"/> no sea exactamente <see cref="Count"/>. En una base de
/// datos alcanza con una clave única sobre el índice; así dos procesos que escriban a la vez
/// nunca bifurcan la cadena.
/// </remarks>
public interface IBlockStore
{
    long Count { get; }
    Block? Last { get; }
    Block Get(long index);
    IEnumerable<Block> ReadAll();
    void Append(Block block);
}

public sealed class BlockStoreConcurrencyException(string message) : InvalidOperationException(message);
