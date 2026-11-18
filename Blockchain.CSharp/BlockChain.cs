using System.Collections;
using System.Security.Cryptography;

namespace Blockchain.CSharp;

/// <summary>
/// Cadena de bloques sólo de agregado: cualquier alteración de un bloque guardado se detecta
/// con <see cref="Validate()"/>. Segura para usar desde varios hilos.
/// </summary>
/// <remarks>
/// Quien tenga acceso de escritura al almacenamiento puede recalcular la cadena entera.
/// Para que la alteración sea detectable también en ese caso, guardá <see cref="HeadHash"/>
/// periódicamente fuera del sistema (otro servidor, un log firmado, un correo) y validá con
/// <see cref="Validate(ReadOnlySpan{byte}, long)"/>.
/// </remarks>
public sealed class BlockChain : IReadOnlyList<Block>
{
    private readonly IBlockStore _store;
    private readonly BlockChainOptions _options;
    private readonly object _writeLock = new();

    public BlockChain(BlockChainOptions? options = null, IBlockStore? store = null)
    {
        _options = options ?? new BlockChainOptions();
        _options.Validate();
        _store = store ?? new InMemoryBlockStore();

        if (_store.Count == 0)
            AppendNew(_options.GenesisData, CancellationToken.None);
    }

    public BlockChainOptions Options => _options;
    public int Count => checked((int)_store.Count);
    public long LongCount => _store.Count;
    public Block this[int index] => _store.Get(index);
    public Block Last => _store.Last!;

    /// <summary>Hash del último bloque: es la "huella" de toda la cadena.</summary>
    public ReadOnlyMemory<byte> HeadHash => Last.Hash;

    /// <summary>Agrega un bloque con los datos dados (se copian) y lo devuelve.</summary>
    public Block Add(byte[] data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return AppendNew(data, cancellationToken);
    }

    private Block AppendNew(byte[] data, CancellationToken cancellationToken)
    {
        // Para cadenas compartidas entre procesos, el almacenamiento rechaza índices duplicados
        // y se reintenta con el nuevo último bloque.
        while (true)
        {
            lock (_writeLock)
            {
                var prev = _store.Last;
                var index = prev is null ? 0 : prev.Index + 1;
                var prevHash = prev is null ? ReadOnlySpan<byte>.Empty : prev.Hash.Span;
                var timestamp = _options.TimeProvider.GetUtcNow();
                if (prev is not null && timestamp < prev.TimeStamp)
                    timestamp = prev.TimeStamp; // el reloj retrocedió: se mantiene el orden

                var (nonce, hash) = BlockHasher.Mine(
                    _options.HashAlgorithm, _options.DifficultyBits, index, timestamp, prevHash, data, cancellationToken);
                var block = new Block(index, timestamp, data, prevHash.ToArray(), nonce, hash);
                try
                {
                    _store.Append(block);
                    return block;
                }
                catch (BlockStoreConcurrencyException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }
    }

    /// <summary>Verifica la cadena completa y, si hay un problema, indica el primer bloque inválido.</summary>
    public ChainValidationResult Validate()
    {
        var expectedLength = BlockHasher.HashLength(_options.HashAlgorithm);
        Block? prev = null;
        long expectedIndex = 0;

        foreach (var block in _store.ReadAll())
        {
            if (block.Index != expectedIndex)
                return ChainValidationResult.Invalid(expectedIndex, $"Índice {block.Index} fuera de secuencia.");

            if (prev is null)
            {
                if (!block.PrevHash.IsEmpty)
                    return ChainValidationResult.Invalid(block.Index, "El génesis no debe tener hash previo.");
            }
            else
            {
                if (!block.PrevHash.Span.SequenceEqual(prev.Hash.Span))
                    return ChainValidationResult.Invalid(block.Index, "El hash previo no coincide con el bloque anterior.");
                if (block.TimeStamp < prev.TimeStamp)
                    return ChainValidationResult.Invalid(block.Index, "La fecha es anterior a la del bloque previo.");
            }

            if (block.Hash.Length != expectedLength)
                return ChainValidationResult.Invalid(block.Index, "Largo de hash inválido para el algoritmo configurado.");
            var computed = BlockHasher.ComputeHash(_options.HashAlgorithm, block);
            if (!CryptographicOperations.FixedTimeEquals(computed, block.Hash.Span))
                return ChainValidationResult.Invalid(block.Index, "El contenido del bloque no coincide con su hash.");
            if (!BlockHasher.MeetsDifficulty(block.Hash.Span, _options.DifficultyBits))
                return ChainValidationResult.Invalid(block.Index, "El hash no cumple la dificultad.");

            prev = block;
            expectedIndex++;
        }

        return expectedIndex == 0
            ? ChainValidationResult.Invalid(0, "La cadena está vacía (falta el génesis).")
            : ChainValidationResult.Valid(expectedIndex);
    }

    /// <summary>
    /// Valida la cadena y además que contenga, en <paramref name="index"/>, el hash guardado
    /// externamente. Detecta que alguien haya recalculado la cadena completa.
    /// </summary>
    public ChainValidationResult Validate(ReadOnlySpan<byte> anchoredHash, long index)
    {
        var result = Validate();
        if (!result.IsValid)
            return result;
        if (index < 0 || index >= _store.Count)
            return ChainValidationResult.Invalid(index, "El bloque anclado no existe: la cadena fue truncada.");
        if (!_store.Get(index).Hash.Span.SequenceEqual(anchoredHash))
            return ChainValidationResult.Invalid(index, "El hash no coincide con el anclado externamente: la cadena fue reescrita.");
        return result;
    }

    public bool IsValid() => Validate().IsValid;

    public IEnumerator<Block> GetEnumerator() => _store.ReadAll().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed record ChainValidationResult(bool IsValid, long BlockCount, long? FirstInvalidIndex, string? Error)
{
    public static ChainValidationResult Valid(long count) => new(true, count, null, null);
    public static ChainValidationResult Invalid(long index, string error) => new(false, 0, index, error);
    public override string ToString() => IsValid ? $"Válida ({BlockCount} bloques)" : $"Inválida en bloque #{FirstInvalidIndex}: {Error}";
}
