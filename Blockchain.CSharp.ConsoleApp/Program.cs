using Blockchain.CSharp;

var ran = new Random();
IBlock genesis = new Block([0x00, 0x00, 0x00, 0x00, 0x00]);
byte[] difficulty = [0x00, 0x00];
BlockChain chain = new(difficulty, genesis);

for (int i = 0; i < 200; i++)
{
    var data = Enumerable.Range(0, 255).Select(p => (byte)ran.Next());
    chain.Add(new Block(data.ToArray()));
    Console.WriteLine(chain.LastOrDefault()?.ToString());
    if (chain.IsValid())
        Console.WriteLine("Blockchain is valid");
}