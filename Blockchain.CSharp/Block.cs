using System.Collections;
using System.Security.Cryptography;

namespace Blockchain.CSharp
{
    public interface IBlock
    {
        byte[]? Data { get; }
        byte[]? Hash { get; set; }
        int Nonce { get; set; }
        byte[]? PrevHash { get; set; }
        DateTime TimeStamp { get; }
    }

    public class Block(byte[] data) : IBlock
    {
        public byte[]? Data { get; } = data ?? throw new ArgumentNullException(nameof(data));

        public byte[]? Hash { get; set; }
        public int Nonce { get; set; } = 0;
        public byte[]? PrevHash { get; set; } = [0x00];
        public DateTime TimeStamp { get; } = DateTime.UtcNow;

        public override string ToString()
        {
            return $"{BitConverter.ToString(Hash!).Replace("-", "")}:\n{BitConverter.ToString(PrevHash!).Replace("-", "")}\n {Nonce}  {TimeStamp}";
        }
    }

    public class BlockChain : IEnumerable<IBlock>
    {
        private List<IBlock> _items = [];

        public BlockChain(byte[] difficulty, IBlock genesis)
        {
            Difficulty = difficulty;
            genesis.Hash = genesis.MineHash(difficulty);
            Items.Add(genesis);
        }

        public void Add(IBlock item)
        {
            if (_items.LastOrDefault() != null)
                item.PrevHash = _items.LastOrDefault()!.Hash;

            item.Hash = item.MineHash(Difficulty);
            Items.Add(item);
        }

        public int Count => Items.Count;
        public IBlock this[int index]
        {
            get => Items[index];
            set => Items[index] = value;
        }
        public List<IBlock> Items
        {
            get => _items;
            set => _items = value;
        }
        public byte[] Difficulty { get; }

        public IEnumerator<IBlock> GetEnumerator()
        {
            return Items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Items.GetEnumerator();
        }
    }

    public static class BlockChainExtension
    {
        public static byte[] GenerateHash(this IBlock block)
        {
            using MemoryStream st = new();
            using BinaryWriter bw = new(st);
            bw.Write(block.Data!);
            bw.Write(block.Nonce);
            bw.Write(block.TimeStamp.ToString());
            bw.Write(block.PrevHash!);
            var starr = st.ToArray();
            return SHA512.HashData(starr);
        }

        public static byte[] MineHash(this IBlock block, byte[] difficulty)
        {
            ArgumentNullException.ThrowIfNull(difficulty);

            byte[] hash = [];

            while (!hash.Take(2).SequenceEqual(difficulty))
            {
                block.Nonce++;
                hash = block.GenerateHash();
            }

            return hash;
        }

        public static bool IsValid(this IBlock block)
        {
            var bk = block.GenerateHash();
            return block.Hash!.SequenceEqual(bk);
        }

        public static bool IsValidPrevBlock(this IBlock block, IBlock prevBlock)
        {
            ArgumentNullException.ThrowIfNull(prevBlock);
            _ = prevBlock.GenerateHash();
            return prevBlock.IsValid() && block.PrevHash!.SequenceEqual(prevBlock.Hash!);
        }

        public static bool IsValid(this IEnumerable<IBlock> items)
        {
            var enums = items.ToList();
            return enums.Zip(enums.Skip(1), Tuple.Create).All(block =>
                block.Item2.IsValid() && block.Item2.IsValidPrevBlock(block.Item2));
        }
    }
}