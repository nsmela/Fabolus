using Fabolus.Core.Meshes.PoissonRecon;

namespace ConsoleTester;

internal class Program {
    static void Main(string[] args) {
        Console.WriteLine("Hello, World!");
        double value = PoissonSmoothingGenerator.Test();
        Console.ReadLine();
    }
}
