using System;
using System.Collections.Generic;
using System.Linq;

interface IMeshCommand {}
abstract record MouldDefinition : IMeshCommand {}
record ConvexMouldDefinition : MouldDefinition {}
record RotateCommand : IMeshCommand {}

class Program {
    static void Main() {
        var cmds = new List<IMeshCommand> { new RotateCommand(), new ConvexMouldDefinition() };
        var filtered = cmds.Where(c => c is not MouldDefinition).ToList();
        Console.WriteLine(filtered.Count);
    }
}
