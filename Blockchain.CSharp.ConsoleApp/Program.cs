using System.Text;
using Blockchain.CSharp;

var path = Path.Combine(Path.GetTempPath(), "blockchain-demo.jsonl");
File.Delete(path);

using (var store = new FileBlockStore(path))
{
    var chain = new BlockChain(new BlockChainOptions { DifficultyBits = 12 }, store);
    for (int i = 1; i <= 5; i++)
        Console.WriteLine(chain.Add(Encoding.UTF8.GetBytes($"Movimiento {i}")));

    Console.WriteLine($"\nValidación: {chain.Validate()}");
    Console.WriteLine($"Hash de cabeza (guardarlo fuera del sistema): {Convert.ToHexString(chain.HeadHash.Span)}");
}

// Reabrir desde disco y validar.
using (var store = new FileBlockStore(path))
{
    var chain = new BlockChain(new BlockChainOptions { DifficultyBits = 12 }, store);
    Console.WriteLine($"Reabierta desde {path}: {chain.Validate()}");
}

// Alterar el archivo a mano y volver a validar.
var lines = File.ReadAllLines(path);
lines[3] = lines[3].Replace(Convert.ToBase64String(Encoding.UTF8.GetBytes("Movimiento 3")),
                            Convert.ToBase64String(Encoding.UTF8.GetBytes("Movimiento X")));
File.WriteAllLines(path, lines);
using (var store = new FileBlockStore(path))
{
    var chain = new BlockChain(new BlockChainOptions { DifficultyBits = 12 }, store);
    Console.WriteLine($"Tras alterar el archivo: {chain.Validate()}");
}
