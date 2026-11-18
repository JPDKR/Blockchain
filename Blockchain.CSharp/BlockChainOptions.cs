namespace Blockchain.CSharp;

public enum BlockHashAlgorithm
{
    Sha256,
    Sha384,
    Sha512,
}

public sealed record BlockChainOptions
{
    /// <summary>Algoritmo de hash de los bloques. No se puede cambiar en una cadena existente.</summary>
    public BlockHashAlgorithm HashAlgorithm { get; init; } = BlockHashAlgorithm.Sha256;

    /// <summary>
    /// Prueba de trabajo: cantidad de bits en cero al inicio del hash (0 = sin prueba de trabajo).
    /// En un sistema con una sola autoridad no agrega seguridad; sólo encarece escribir.
    /// Cada bit duplica el costo promedio de minar un bloque.
    /// </summary>
    public int DifficultyBits { get; init; } = 0;

    /// <summary>Datos del bloque génesis cuando la cadena se crea vacía.</summary>
    public byte[] GenesisData { get; init; } = [];

    /// <summary>Reloj inyectable (útil para pruebas).</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    internal void Validate()
    {
        var maxBits = BlockHasher.HashLength(HashAlgorithm) * 8;
        if (DifficultyBits < 0 || DifficultyBits > maxBits)
            throw new ArgumentOutOfRangeException(nameof(DifficultyBits), DifficultyBits, $"Debe estar entre 0 y {maxBits}.");
        // Más de 32 bits es impracticable para minar en CPU.
        if (DifficultyBits > 32)
            throw new ArgumentOutOfRangeException(nameof(DifficultyBits), DifficultyBits, "Máximo soportado: 32 bits.");
        ArgumentNullException.ThrowIfNull(GenesisData);
        ArgumentNullException.ThrowIfNull(TimeProvider);
    }
}
