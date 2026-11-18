using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Blockchain.CSharp;
using Xunit;

namespace Blockchain.CSharp.Tests;

public class BlockChainTests
{
    private static BlockChain Build(int blocks, BlockChainOptions? options = null, IBlockStore? store = null)
    {
        var chain = new BlockChain(options, store);
        for (int i = 0; i < blocks; i++)
            chain.Add(Encoding.UTF8.GetBytes($"dato {i}"));
        return chain;
    }

    /// <summary>Store que permite simular a un atacante con acceso de escritura al almacenamiento.</summary>
    private sealed class TamperableStore : IBlockStore
    {
        public List<Block> Blocks { get; } = [];
        public long Count => Blocks.Count;
        public Block? Last => Blocks.Count == 0 ? null : Blocks[^1];
        public Block Get(long index) => Blocks[(int)index];
        public IEnumerable<Block> ReadAll() => Blocks.ToArray();
        public void Append(Block block)
        {
            if (block.Index != Blocks.Count) throw new BlockStoreConcurrencyException("índice");
            Blocks.Add(block);
        }
    }

    private static Block With(Block b, byte[]? data = null, byte[]? prevHash = null, byte[]? hash = null,
        long? index = null, DateTimeOffset? ts = null, long? nonce = null) =>
        new(index ?? b.Index, ts ?? b.TimeStamp, data ?? b.Data.ToArray(), prevHash ?? b.PrevHash.ToArray(),
            nonce ?? b.Nonce, hash ?? b.Hash.ToArray());

    private static Block Rehash(Block b, BlockHashAlgorithm alg = BlockHashAlgorithm.Sha256) =>
        With(b, hash: BlockHasher.ComputeHash(alg, b.Index, b.TimeStamp, b.Nonce, b.PrevHash.Span, b.Data.Span));

    // ---------- Funcionamiento normal ----------

    [Fact]
    public void New_chain_has_valid_genesis()
    {
        var chain = new BlockChain();
        Assert.Single(chain);
        Assert.Equal(0, chain[0].Index);
        Assert.True(chain[0].PrevHash.IsEmpty);
        Assert.True(chain.IsValid());
    }

    [Fact]
    public void Untampered_chain_is_valid()
    {
        var chain = Build(50);
        var result = chain.Validate();
        Assert.True(result.IsValid, result.ToString());
        Assert.Equal(51, result.BlockCount);
    }

    [Fact]
    public void Blocks_are_linked_and_indexed()
    {
        var chain = Build(5);
        for (int i = 1; i < chain.Count; i++)
        {
            Assert.Equal(i, chain[i].Index);
            Assert.True(chain[i].PrevHash.Span.SequenceEqual(chain[i - 1].Hash.Span));
        }
    }

    [Theory]
    [InlineData(BlockHashAlgorithm.Sha256, 32)]
    [InlineData(BlockHashAlgorithm.Sha384, 48)]
    [InlineData(BlockHashAlgorithm.Sha512, 64)]
    public void Supports_hash_algorithms(BlockHashAlgorithm alg, int length)
    {
        var chain = Build(3, new BlockChainOptions { HashAlgorithm = alg });
        Assert.All(chain, b => Assert.Equal(length, b.Hash.Length));
        Assert.True(chain.IsValid());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(17)]
    public void Proof_of_work_works_for_any_bit_count(int bits)
    {
        var chain = Build(2, new BlockChainOptions { DifficultyBits = bits });
        Assert.All(chain, b => Assert.True(BlockHasher.MeetsDifficulty(b.Hash.Span, bits)));
        Assert.True(chain.IsValid());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(33)]
    public void Invalid_difficulty_is_rejected_immediately(int bits) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlockChain(new BlockChainOptions { DifficultyBits = bits }));

    [Fact]
    public void Mining_can_be_cancelled()
    {
        var store = new InMemoryBlockStore();
        _ = new BlockChain(store: store); // génesis rápido
        var slow = new BlockChain(new BlockChainOptions { DifficultyBits = 32 }, store);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        Assert.ThrowsAny<OperationCanceledException>(() => slow.Add([1], cts.Token));
    }

    [Fact]
    public void Hash_is_independent_of_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-AR");
            var chain = Build(3);
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.True(chain.IsValid());
            CultureInfo.CurrentCulture = new CultureInfo("ja-JP");
            Assert.True(chain.IsValid());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Block_is_immutable_from_outside()
    {
        var data = new byte[] { 1, 2, 3 };
        var chain = new BlockChain();
        var block = chain.Add(data);
        data[0] = 99; // modificar el arreglo original no afecta al bloque
        Assert.Equal(1, block.Data.Span[0]);
        Assert.True(chain.IsValid());
    }

    [Fact]
    public void Concurrent_adds_keep_chain_valid()
    {
        var chain = new BlockChain();
        Parallel.For(0, 500, i => chain.Add(BitConverter.GetBytes(i)));
        Assert.Equal(501, chain.Count);
        Assert.True(chain.IsValid());
    }

    [Fact]
    public void Clock_going_backwards_keeps_order()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var chain = new BlockChain(new BlockChainOptions { TimeProvider = clock });
        clock.Now = clock.Now.AddHours(-1);
        chain.Add([1]);
        Assert.True(chain.IsValid());
    }

    [Fact]
    public void Subsecond_timestamp_is_part_of_hash()
    {
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var a = BlockHasher.ComputeHash(BlockHashAlgorithm.Sha256, 0, t, 0, [], [1]);
        var b = BlockHasher.ComputeHash(BlockHashAlgorithm.Sha256, 0, t.AddTicks(1), 0, [], [1]);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Field_boundaries_are_unambiguous()
    {
        // prevHash=[1], data=[2] no debe dar lo mismo que prevHash=[], data=[1,2]
        var t = DateTimeOffset.UnixEpoch;
        var a = BlockHasher.ComputeHash(BlockHashAlgorithm.Sha256, 0, t, 0, [1], [2]);
        var b = BlockHasher.ComputeHash(BlockHashAlgorithm.Sha256, 0, t, 0, [], [1, 2]);
        Assert.NotEqual(a, b);
    }

    // ---------- Detección de alteraciones ----------

    [Fact]
    public void Tampered_data_is_detected()
    {
        var store = new TamperableStore();
        Build(5, store: store);
        store.Blocks[3] = With(store.Blocks[3], data: [9, 9, 9]);
        var r = new BlockChain(store: store).Validate();
        Assert.False(r.IsValid);
        Assert.Equal(3, r.FirstInvalidIndex);
    }

    [Fact]
    public void Tampered_genesis_is_detected()
    {
        var store = new TamperableStore();
        Build(2, store: store);
        store.Blocks[0] = With(store.Blocks[0], data: [1]);
        Assert.Equal(0, new BlockChain(store: store).Validate().FirstInvalidIndex);
    }

    [Fact]
    public void Rehashed_block_breaks_link_to_next()
    {
        var store = new TamperableStore();
        Build(5, store: store);
        store.Blocks[2] = Rehash(With(store.Blocks[2], data: [7])); // hash propio correcto
        var r = new BlockChain(store: store).Validate();
        Assert.False(r.IsValid);
        Assert.Equal(3, r.FirstInvalidIndex); // el siguiente ya no apunta a él
    }

    [Fact]
    public void Deleted_block_is_detected()
    {
        var store = new TamperableStore();
        Build(5, store: store);
        store.Blocks.RemoveAt(2);
        Assert.False(new BlockChain(store: store).IsValid());
    }

    [Fact]
    public void Reordered_blocks_are_detected()
    {
        var store = new TamperableStore();
        Build(5, store: store);
        (store.Blocks[2], store.Blocks[3]) = (store.Blocks[3], store.Blocks[2]);
        Assert.False(new BlockChain(store: store).IsValid());
    }

    [Fact]
    public void Lowered_difficulty_is_detected()
    {
        var store = new TamperableStore();
        Build(3, new BlockChainOptions { DifficultyBits = 8 }, store);
        store.Blocks[3] = Rehash(With(store.Blocks[3], data: [5], nonce: 0));
        // Si el hash re-calculado no cumple la dificultad, se detecta aunque el enlace siguiente no exista.
        var r = new BlockChain(new BlockChainOptions { DifficultyBits = 8 }, store).Validate();
        Assert.True(!r.IsValid || BlockHasher.MeetsDifficulty(store.Blocks[3].Hash.Span, 8));
    }

    [Fact]
    public void Full_rewrite_is_detected_only_with_external_anchor()
    {
        var store = new TamperableStore();
        var chain = Build(5, store: store);
        var anchor = chain.HeadHash.ToArray();
        var anchorIndex = chain.Last.Index;

        // El atacante altera el bloque 2 y recalcula todo lo que sigue.
        store.Blocks[2] = With(store.Blocks[2], data: [6]);
        for (int i = 2; i < store.Blocks.Count; i++)
        {
            var prev = i == 0 ? [] : store.Blocks[i - 1].Hash.ToArray();
            store.Blocks[i] = Rehash(With(store.Blocks[i], prevHash: prev));
        }

        var rewritten = new BlockChain(store: store);
        Assert.True(rewritten.Validate().IsValid);                    // sin ancla no se puede saber
        Assert.False(rewritten.Validate(anchor, anchorIndex).IsValid); // con ancla externa, sí
    }

    [Fact]
    public void Truncation_is_detected_with_external_anchor()
    {
        var store = new TamperableStore();
        var chain = Build(5, store: store);
        var anchor = chain.HeadHash.ToArray();
        store.Blocks.RemoveRange(4, 2);
        Assert.False(new BlockChain(store: store).Validate(anchor, 5).IsValid);
    }

    // ---------- Persistencia en archivo ----------

    [Fact]
    public void File_store_persists_and_reloads()
    {
        var path = Path.GetTempFileName();
        try
        {
            string head;
            using (var s = new FileBlockStore(path)) head = Build(10, store: s).Last.HashHex;
            using (var s = new FileBlockStore(path))
            {
                var chain = new BlockChain(store: s);
                Assert.Equal(11, chain.Count);
                Assert.Equal(head, chain.Last.HashHex);
                Assert.True(chain.IsValid());
                chain.Add([1]);
            }
            using (var s = new FileBlockStore(path)) Assert.True(new BlockChain(store: s).IsValid());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void File_store_detects_edited_file()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var s = new FileBlockStore(path)) Build(3, store: s);
            var lines = File.ReadAllLines(path);
            var node = JsonNode.Parse(lines[2])!;
            node["Data"] = Convert.ToBase64String([42]);
            lines[2] = node.ToJsonString();
            File.WriteAllLines(path, lines);
            using var s2 = new FileBlockStore(path);
            Assert.Equal(2, new BlockChain(store: s2).Validate().FirstInvalidIndex);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void File_store_recovers_from_truncated_last_line()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var s = new FileBlockStore(path)) Build(3, store: s);
            var bytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path, bytes[..^20]); // corte a mitad de escritura
            using (var s = new FileBlockStore(path))
            {
                var chain = new BlockChain(store: s);
                Assert.Equal(3, chain.Count);
                Assert.True(chain.IsValid());
                chain.Add([1]);
            }
            using (var s = new FileBlockStore(path)) Assert.True(new BlockChain(store: s).IsValid());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void File_store_handles_missing_trailing_newline()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var s = new FileBlockStore(path)) Build(2, store: s);
            var bytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path, bytes[..^1]);
            using (var s = new FileBlockStore(path)) new BlockChain(store: s).Add([1]);
            using (var s = new FileBlockStore(path))
            {
                var chain = new BlockChain(store: s);
                Assert.Equal(4, chain.Count);
                Assert.True(chain.IsValid());
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void File_store_is_exclusive_for_writers()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var s = new FileBlockStore(path);
            Assert.Throws<IOException>(() => new FileBlockStore(path));
        }
        finally { File.Delete(path); }
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
