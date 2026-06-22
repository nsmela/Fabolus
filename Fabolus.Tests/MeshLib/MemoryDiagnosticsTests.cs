using System;
using System.Diagnostics;
using System.Numerics;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry;
using GeometryMeshLib;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.MeshLib
{
    [Trait("Category", "Slow")]
    [Trait("Category", "Memory")]
    public class MemoryDiagnosticsTests
    {
        private readonly ITestOutputHelper _output;

        public MemoryDiagnosticsTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Pipeline_WorkingSetGrows_UntilFinalizersRun()
        {
            var fileSystem = new TestFileSystem();
            var engine = new GeometryEngine(fileSystem);

            // GC beforehand to get a clean baseline
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long initialMemory = Process.GetCurrentProcess().WorkingSet64;
            _output.WriteLine($"Initial Working Set: {initialMemory / 1024 / 1024} MB");

            for (int i = 0; i < 200; i++)
            {
                // Generate
                var mesh = engine.Generators.GenerateSphere(Vector3.Zero, 10f, 32).Value;
                
                // Offset
                var offset = engine.Modifiers.OffsetDouble(mesh, 0.5f, 1).Value;
                
                // Translate copy
                var translated = engine.Transforms.Translate(mesh, 5, 0, 0).Value;
                
                // Union
                var union = engine.Booleans.Union(offset, translated).Value;
                
                // Transform
                var resized = engine.Transforms.Scale(union, 1.1).Value;
                
                // We discard results, expecting garbage collection to handle them.
                // But without IDisposable, native memory relies on finalizers.
            }

            long afterLoopMemory = Process.GetCurrentProcess().WorkingSet64;
            _output.WriteLine($"After Loop Working Set: {afterLoopMemory / 1024 / 1024} MB");

            // Assert significant growth (e.g. > 50MB)
            Assert.True(afterLoopMemory - initialMemory > 50 * 1024 * 1024, "Working set did not grow as expected during the loop.");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long afterGcMemory = Process.GetCurrentProcess().WorkingSet64;
            _output.WriteLine($"After GC Working Set: {afterGcMemory / 1024 / 1024} MB");

            // Assert memory was reclaimed by finalizers
            Assert.True(afterLoopMemory - afterGcMemory > 50 * 1024 * 1024, "Finalizers did not reclaim the expected amount of memory.");
        }
    }
}
