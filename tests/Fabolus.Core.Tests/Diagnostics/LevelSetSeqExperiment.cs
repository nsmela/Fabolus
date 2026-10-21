using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Diagnostics;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Core.Tests.Diagnostics;

/// <summary>
/// Why a replayed body is never the same twice. The engine's offset hands the body's distance field to
/// Manifold's level-set mesher, which runs in parallel; this calls Manifold directly with the same
/// field both ways. On larynx_small the parallel mesher gave three different meshes in three calls
/// (different even with the vertices sorted, so not just a reordering) and the sequential one gave
/// the same mesh each time. GeometryEngine calls the parallel one.
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class LevelSetSeqExperiment(GeometryEngineFixture fixture, ITestOutputHelper output)
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double Sdf(double x, double y, double z, IntPtr context);

    [DllImport("manifoldc")] private static extern IntPtr manifold_alloc_box();
    [DllImport("manifoldc")] private static extern IntPtr manifold_box(IntPtr mem, double x1, double y1, double z1, double x2, double y2, double z2);
    [DllImport("manifoldc")] private static extern IntPtr manifold_alloc_manifold();
    [DllImport("manifoldc")] private static extern IntPtr manifold_level_set(IntPtr mem, Sdf sdf, IntPtr bounds, double edge, double level, double tol, IntPtr ctx);
    [DllImport("manifoldc")] private static extern IntPtr manifold_level_set_seq(IntPtr mem, Sdf sdf, IntPtr bounds, double edge, double level, double tol, IntPtr ctx);
    [DllImport("manifoldc")] private static extern IntPtr manifold_alloc_meshgl64();
    [DllImport("manifoldc")] private static extern IntPtr manifold_get_meshgl64(IntPtr mem, IntPtr m);
    [DllImport("manifoldc")] private static extern nuint manifold_meshgl64_num_vert(IntPtr m);
    [DllImport("manifoldc")] private static extern nuint manifold_meshgl64_num_prop(IntPtr m);
    [DllImport("manifoldc")] private static extern nuint manifold_meshgl64_vert_properties_length(IntPtr m);
    [DllImport("manifoldc")] private static extern IntPtr manifold_meshgl64_vert_properties(IntPtr mem, IntPtr m);

    [DiagnosticTheory]
    [InlineData("3mf/larynx_small.3mf")]
    public void ParallelVersusSequential(string file)
    {
        var engine = fixture.Engine;
        var opened = new Fabolus.Core.Features.MeshIO.ImportMesh(engine).Execute(Workspace.CreateEmpty(), Asset(file)).Value;
        var input = engine.Modifiers.Offset(opened.GetActiveRecord().Value.BaseMesh!, 1.0, 1.0).Value;   // the grown mesh
        var index = engine.Spatial.IndexFor(input).Value;

        var min = input.Vertices[0]; var max = input.Vertices[0];
        foreach (var v in input.Vertices) { min = min.ComponentMin(v); max = max.ComponentMax(v); }
        double cell = 1.0;
        var pad = new Vector3(1, 1, 1) * (1.0 + 2 * cell);
        min -= pad; max += pad;

        Sdf sdf = (x, y, z, _) => -index.SignedDistance(new Vector3(x, y, z));
        foreach (bool seq in new[] { false, true })
        {
            for (int run = 0; run < 3; run++)
            {
                var box = manifold_box(manifold_alloc_box(), min.X, min.Y, min.Z, max.X, max.Y, max.Z);
                var solid = seq
                    ? manifold_level_set_seq(manifold_alloc_manifold(), sdf, box, cell, 1.0, 0, IntPtr.Zero)
                    : manifold_level_set(manifold_alloc_manifold(), sdf, box, cell, 1.0, 0, IntPtr.Zero);
                var gl = manifold_get_meshgl64(manifold_alloc_meshgl64(), solid);
                int n = (int)manifold_meshgl64_vert_properties_length(gl);
                var buffer = Marshal.AllocHGlobal(n * sizeof(double));
                manifold_meshgl64_vert_properties(buffer, gl);
                var props = new double[n];
                Marshal.Copy(buffer, props, 0, n);
                int stride = (int)manifold_meshgl64_num_prop(gl);
                var sorted = Enumerable.Range(0, n / stride).Select(i => (props[i * stride], props[i * stride + 1], props[i * stride + 2])).OrderBy(p => p)
                    .SelectMany(p => new[] { p.Item1, p.Item2, p.Item3 });
                output.WriteLine($"{(seq ? "seq" : "par")} run {run}: {manifold_meshgl64_num_vert(gl)}v plain {H(props)} sorted {H(sorted)}");
            }
        }
        GC.KeepAlive(sdf);
    }

    private static string H(IEnumerable<double> values) =>
        Convert.ToHexString(SHA256.HashData(values.SelectMany(BitConverter.GetBytes).ToArray()))[..10];

    private static string Asset(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "files"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tests", "files", name);
    }
}
