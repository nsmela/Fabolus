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
}
