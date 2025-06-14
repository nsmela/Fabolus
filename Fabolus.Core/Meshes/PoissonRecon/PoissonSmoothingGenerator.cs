using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.PoissonRecon;

/// <summary>
/// Wrapper class for Poison Reconstruction dll
/// </summary>
public class PoissonSmoothingGenerator {

    public static double Test() => PoissonReconDLLAdapter.PeakMemoryUsageMB();

    private static class PoissonReconDLLAdapter {
        // ref: https://www.youtube.com/watch?v=fXO7rU2-qu8
        // https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke
        // https://www.vishalon.net/blog/cpp-dll-get-exported-symbols-list-on-windows
        // dll is PoissonRecon version 9.011
        // https://github.com/mkazhdan/PoissonRecon/tree/16375a78928bd67b4cad1a410131f4b5f0afabde

        [DllImport("PoissonRecon.dll", EntryPoint = "PeakMemoryUsageMB")]
        public static extern double PeakMemoryUsageMB();
    }
}
