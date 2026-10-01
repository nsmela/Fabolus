using System.Collections.Immutable;
using System.Threading;
using BasicResults;
using Fabolus.Core.Features.Decal;
using Fabolus.Wpf.Features.Decal;
using GeometryEngine.Core.Geometry;
using Xunit;

using SnVector3 = System.Numerics.Vector3;

namespace Fabolus.Wpf.Tests.Features.Decal;

/// <summary>
/// Every glyph the app can emboss has to build a sound prism.
/// </summary>
/// <remarks>
/// This is the one decal test that has to live in the WPF project: the outlines come from
/// WpfGlyphOutlineSource, which renders them with WPF's own text stack and needs an STA thread.
/// GeometryEngine tests its prism builder against outlines it makes itself, so nothing else
/// checks that what a real font hands over is something the builder can close.
/// </remarks>
public class GlyphMeshTests
{
    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    private static void RunInSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    private static Result<IMesh> BuildPrism(IReadOnlyList<Polygon2D> outlines)
    {
        var frame = DecalFrame.FromHit(SnVector3.Zero, SnVector3.UnitZ, 0f);

        return Engine.Decals.BuildPrism(new DecalPrismSpec(
            outlines.ToImmutableArray(),
            new SurfaceFrame(
                new Vector3(frame.Origin.X, frame.Origin.Y, frame.Origin.Z),
                new Vector3(frame.U.X, frame.U.Y, frame.U.Z),
                new Vector3(frame.V.X, frame.V.Y, frame.V.Z),
                new Vector3(frame.N.X, frame.N.Y, frame.N.Z)),
            Depth: 0.8,
            Sink: -0.05,
            Overshoot: 0.05,
            MaxEdgeLength: 0.5));
    }

    private void AssertSoundPrism(IMesh mesh, string what)
    {
        Assert.True(mesh.TriangleCount > 0, $"Mesh has 0 triangles for {what}");

        var topology = Engine.Evaluators.ValidateTopology(mesh);
        if (topology.IsFailure)
            Assert.Fail($"Topology validation failed for {what}: {topology.Error.Description}");

        Assert.True(topology.Value.IsManifold, $"{what} is NOT manifold!");
        Assert.True(topology.Value.IsWatertight, $"{what} is NOT watertight!");
        Assert.Equal(0, topology.Value.DegenerateTriangleCount);

        var selfIntersections = Engine.Evaluators.CountSelfIntersections(mesh);
        if (selfIntersections.IsSuccess)
            Assert.Equal(0, selfIntersections.Value);

        // The slit that joins a counter to the letter's edge used to visit its two ends twice, as
        // separate points in the same place. The prism took the slit for an outline and stood a
        // wall either side of it - inside the letter, invisible on a solid and plain through the
        // translucent preview. Every point of a sound prism is somewhere of its own.
        int distinct = mesh.Vertices.Distinct().Count();
        Assert.True(distinct == mesh.VertexCount, $"{what} has {mesh.VertexCount - distinct} vertices doubled up, and walls inside it.");

        // Each point then starts exactly one outline edge, walled with two triangles.
        var t = mesh.Triangles;
        int walls = Enumerable.Range(0, t.Length / 3)
            .Count(i => (t[i * 3] % 2) + (t[i * 3 + 1] % 2) + (t[i * 3 + 2] % 2) is 1 or 2);
        Assert.True(walls == mesh.VertexCount, $"{what} has {walls - mesh.VertexCount} wall triangles off its outlines.");
    }

    [Theory]
    [InlineData("FABOLUS")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ")]
    [InlineData("abcdefghijklmnopqrstuvwxyz")]
    [InlineData("0123456789")]
    [InlineData("!@#$%&*()-_+=[]{}|:;,.?")]
    public void EveryGlyph_BuildsACleanManifoldPrism(string characters)
    {
        RunInSta(() =>
        {
            var outlineSource = new WpfGlyphOutlineSource();

            foreach (char c in characters)
            {
                string text = c.ToString();
                foreach (var font in new[] { DecalFont.Sans, DecalFont.Mono, DecalFont.Bold })
                {
                    var outlines = outlineSource.GetOutlines(text, font, capHeight: 6.0f, tracking: 0.4f);
                    if (outlines.IsFailure || outlines.Value.Count == 0) continue;

                    var prism = BuildPrism(outlines.Value);
                    if (prism.IsFailure)
                        Assert.Fail($"Failed to build prism for character '{c}' ({font}): {prism.Error.Description}");

                    AssertSoundPrism(prism.Value, $"character '{c}' ({font})");
                }
            }
        });
    }

    [Fact]
    public void AWholeWord_BuildsACleanManifoldPrism()
    {
        RunInSta(() =>
        {
            var outlineSource = new WpfGlyphOutlineSource();

            var outlines = outlineSource.GetOutlines("FABOLUS", DecalFont.Sans, capHeight: 6.0f, tracking: 0.4f);
            Assert.True(outlines.IsSuccess);
            Assert.NotEmpty(outlines.Value);

            var prism = BuildPrism(outlines.Value);
            Assert.True(prism.IsSuccess, prism.IsFailure ? prism.Error.Description : string.Empty);

            AssertSoundPrism(prism.Value, "the word FABOLUS");
        });
    }

    /// <summary>
    /// Wrapped onto a real scan, the label's top must not turn over anywhere. It used to on
    /// nearly every placement - neighbouring points settled onto different facets landed out of
    /// order or leaned apart - and showed as holes in the letters through to the model, while
    /// every topology check above still passed.
    /// </summary>
    [Fact]
    public void AWholeWord_WrappedOntoAScan_KeepsItsCapTheRightWayUp()
    {
        RunInSta(() =>
        {
            var outlineSource = new WpfGlyphOutlineSource();
            var scan = Engine.IO.Import(PathTo("scalp_bolus.stl")).Value;
            var surface = DecalSurface.For(Engine, scan).Value;

            // As the preview builds it: a 10mm bold label, edges no longer than an eighth of that.
            const float capHeight = 10f;
            const float maxEdge = capHeight / 8f;
            var outlines = outlineSource.GetOutlines("FABOLUS", DecalFont.Bold, capHeight, TextDecal.DefaultTracking).Value;
            var flat = Engine.Decals.BuildPrism(new DecalPrismSpec(
                outlines.ToImmutableArray(),
                new SurfaceFrame(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
                0.8, -0.05, 0.05, maxEdge)).Value;

            var down = GeometryEngine.Core.Geometry.Primitives.Direction.From(-Vector3.UnitZ).Value;
            int placements = 0;
            for (double x = -45; x <= 45; x += 15)
            {
                for (double y = -55; y <= -10; y += 15)
                {
                    var hit = surface.Index.Raycast(new Vector3(x, y, 300), down);
                    if (!hit.HasValue || Math.Abs(hit.Value.Normal.Z) < 0.6)
                        continue; // Down the side, where the label would hang off the rim.

                    var normal = hit.Value.Normal.Z < 0 ? -hit.Value.Normal : hit.Value.Normal;
                    foreach (var rotation in new[] { 0f, 37f, 90f })
                    {
                        var request = new DecalPrismRequest("FABOLUS", DecalFont.Bold, capHeight, TextDecal.DefaultTracking,
                            hit.Value.Point, normal, rotation, 0.8f, -0.05f, 0.05f, maxEdge);
                        var prism = surface.GetOrBuildPrism(Engine, outlineSource, request);
                        Assert.True(prism.IsSuccess, prism.IsFailure ? prism.Error.Description : string.Empty);

                        // A label long enough to run over the rim wraps round a right angle
                        // there, and no cap stays flat round that. Only labels that lie on the
                        // dome are a fair test.
                        if (WrapsOverAnEdge(prism.Value, normal))
                            continue;

                        AssertCapIsTheRightWayUp(prism.Value, flat, $"at ({x}, {y}) turned {rotation} degrees");
                        placements++;
                    }
                }
            }

            Assert.True(placements >= 30, $"Only {placements} placements landed on the scan.");
        });
    }

    /// <summary>
    /// Every top cap triangle faces the way its columns rise. <paramref name="flat"/> is the same
    /// prism on a plane, with the same vertex order, so slivers can be told apart: their
    /// orientation is rounding noise, and they have no area to show a hole through.
    /// </summary>
    private static void AssertCapIsTheRightWayUp(IMesh prism, IMesh flat, string where)
    {
        const double minimumAltitude = 0.05;

        var v = prism.Vertices;
        var t = prism.Triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            int a = t[i], b = t[i + 1], c = t[i + 2];

            // The builder interleaves a bottom and a top copy of every point, so a triangle whose
            // corners are all odd is on the top cap.
            if (a % 2 == 0 || b % 2 == 0 || c % 2 == 0)
                continue;

            var (fa, fb, fc) = (flat.Vertices[a], flat.Vertices[b], flat.Vertices[c]);
            double longest = Math.Max((fb - fa).Length, Math.Max((fc - fb).Length, (fa - fc).Length));
            double planarArea = (fb - fa).Cross(fc - fa).Length / 2;
            if (2 * planarArea / longest < minimumAltitude)
                continue;

            var rise = (v[a] - v[a - 1]) + (v[b] - v[b - 1]) + (v[c] - v[c - 1]);
            var facing = (v[b] - v[a]).Cross(v[c] - v[a]);
            Assert.True(facing.Dot(rise) > 0, $"A cap triangle of area {planarArea:F3} mm² turned over {where}.");
        }
    }

    /// <summary>True when some column of the prism leans more than 60 degrees from where the label was placed.</summary>
    private static bool WrapsOverAnEdge(IMesh prism, Vector3 placedNormal)
    {
        var v = prism.Vertices;
        for (int i = 0; i < v.Length; i += 2)
        {
            var column = v[i + 1] - v[i];
            if (column.Dot(placedNormal) < 0.5 * column.Length)
                return true;
        }

        return false;
    }

    private static string PathTo(string name)
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = System.IO.Path.Combine(directory.FullName, "files", name);
            if (System.IO.File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new System.IO.FileNotFoundException($"Could not find tests/files/{name}.");
    }
}
