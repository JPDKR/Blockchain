using System.Text.Json;

namespace Blockchain.CSharp;

/// <summary>Almacenamiento en memoria, seguro para hilos.</summary>
public sealed class InMemoryBlockStore : IBlockStore
{
    private readonly List<Block> _blocks = [];
    private readonly object _lock = new();

    public long Count { get { lock (_lock) return _blocks.Count; } }
    public Block? Last { get { lock (_lock) return _blocks.Count == 0 ? null : _blocks[^1]; } }

    public Block Get(long index) { lock (_lock) return _blocks[checked((int)index)]; }

    public IEnumerable<Block> ReadAll() { lock (_lock) return _blocks.ToArray(); }

    public void Append(Block block)
    {
        ArgumentNullException.ThrowIfNull(block);
        lock (_lock)
        {
            if (block.Index != _blocks.Count)
                throw new BlockStoreConcurrencyException($"Se esperaba el índice {_blocks.Count} y llegó {block.Index}.");
            _blocks.Add(block);
        }
    }
}

/// <summary>
/// Almacenamiento en archivo JSON Lines (un bloque por línea), sólo de agregado y con escritura
/// a disco forzada en cada bloque. Toma el archivo en exclusiva: un único proceso por archivo.
/// Una línea final incompleta (corte de luz a mitad de escritura) se descarta al abrir.
/// </summary>
public sealed class FileBlockStore : IBlockStore, IDisposable
{
    private readonly FileStream _stream;
    private readonly InMemoryBlockStore _cache = new();
    private readonly object _lock = new();

    public FileBlockStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        Load();
    }

    public long Count => _cache.Count;
    public Block? Last => _cache.Last;
    public Block Get(long index) => _cache.Get(index);
    public IEnumerable<Block> ReadAll() => _cache.ReadAll();

    public void Append(Block block)
    {
        lock (_lock)
        {
            if (block.Index != _cache.Count)
                throw new BlockStoreConcurrencyException($"Se esperaba el índice {_cache.Count} y llegó {block.Index}.");
            var line = JsonSerializer.SerializeToUtf8Bytes(Dto.From(block));
            _stream.Seek(0, SeekOrigin.End);
            _stream.Write(line);
            _stream.WriteByte((byte)'\n');
            _stream.Flush(flushToDisk: true);
            _cache.Append(block);
        }
    }

    public void Dispose() => _stream.Dispose();

    private void Load()
    {
        _stream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(_stream, leaveOpen: true);
        long validLength = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            Block block;
            try
            {
                block = JsonSerializer.Deserialize<Dto>(line)!.ToBlock();
            }
            catch (JsonException) when (reader.Peek() < 0)
            {
                break; // última línea truncada: se descarta
            }
            _cache.Append(block);
            validLength += System.Text.Encoding.UTF8.GetByteCount(line) + 1;
        }
        if (validLength < _stream.Length)
        {
            _stream.SetLength(validLength);
        }
        else if (validLength > _stream.Length)
        {
            // La última línea quedó completa pero sin salto de línea.
            _stream.Seek(0, SeekOrigin.End);
            _stream.WriteByte((byte)'\n');
            _stream.Flush(flushToDisk: true);
        }
    }

    private sealed record Dto(long Index, long TimeStampUtcTicks, long Nonce, string Data, string PrevHash, string Hash)
    {
        public static Dto From(Block b) => new(
            b.Index, b.TimeStamp.UtcTicks, b.Nonce,
            Convert.ToBase64String(b.Data.Span), Convert.ToBase64String(b.PrevHash.Span), Convert.ToBase64String(b.Hash.Span));

        public Block ToBlock() => new(
            Index, new DateTimeOffset(TimeStampUtcTicks, TimeSpan.Zero),
            Convert.FromBase64String(Data), Convert.FromBase64String(PrevHash), Nonce, Convert.FromBase64String(Hash));
    }
}
