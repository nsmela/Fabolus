using System.Numerics;
using System.Threading;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Features.Decal;
using GeometryMeshLib;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.Emboss;

public class GlyphMeshTests
{
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

        if (exception != null)
            throw exception;
    }

    [Theory]
    [InlineData("FABOLUS")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ")]
    [InlineData("abcdefghijklmnopqrstuvwxyz")]
    [InlineData("0123456789")]
    [InlineData("!@#$%&*()-_+=[]{}|:;,.?")]
    public void BuildTextPrism_AllAlphanumericCharacters_GeneratesCleanManifoldMesh(string characters)
    {
        RunInSta(() =>
        {
            var engine = new GeometryEngine(new Mock<IFileSystem>().Object);
            var outlineSource = new WpfGlyphOutlineSource();

            foreach (char c in characters)
            {
                string text = c.ToString();
                foreach (var font in new[] { DecalFont.Sans, DecalFont.Mono, DecalFont.Bold })
                {
                    var outlineResult = outlineSource.GetOutlines(text, font, capHeight: 6.0f, tracking: 0.4f);
                    if (outlineResult.IsFailure || outlineResult.Value.Count == 0) continue;

                    var outlines = outlineResult.Value;
                    var frame = DecalFrame.FromHit(Vector3.Zero, Vector3.UnitZ, 0f);
                    var prismResult = engine.Generators.BuildTextPrism(
                        outlines,
                        frame,
                        depth: 0.8f,
                        sink: -0.05f,
                        overshoot: 0.05f,
                        maxEdgeLength: 0.5f);

                    if (prismResult.IsFailure)
                        Assert.Fail($"Failed to build prism for character '{c}' ({font}): {prismResult.Error.Description}");

                    var mesh = prismResult.Value;
                    Assert.True(mesh.TriangleCount > 0, $"Mesh has 0 triangles for '{c}' ({font})");

                    var topoResult = engine.Evaluators.ValidateTopology(mesh);
                    if (topoResult.IsFailure)
                        Assert.Fail($"Topology validation failed for '{c}' ({font}): {topoResult.Error.Description}");

                    var topo = topoResult.Value;
                    Assert.True(topo.IsManifold, $"Character '{c}' ({font}) is NOT manifold!");
                    Assert.True(topo.IsWatertight, $"Character '{c}' ({font}) is NOT watertight!");
                    Assert.Equal(0, topo.SelfIntersectionCount);
                    Assert.False(topo.HasDegenerateTriangles, $"Character '{c}' ({font}) has degenerate triangles!");
                }
            }
        });
    }

    [Fact]
    public void BuildTextPrism_WordFabolus_GeneratesCleanManifoldMesh()
    {
        RunInSta(() =>
        {
            var engine = new GeometryEngine(new Mock<IFileSystem>().Object);
            var outlineSource = new WpfGlyphOutlineSource();

            var outlineResult = outlineSource.GetOutlines("FABOLUS", DecalFont.Sans, capHeight: 6.0f, tracking: 0.4f);
            Assert.True(outlineResult.IsSuccess);
            Assert.NotEmpty(outlineResult.Value);

            var frame = DecalFrame.FromHit(Vector3.Zero, Vector3.UnitZ, 0f);
            var prismResult = engine.Generators.BuildTextPrism(
                outlineResult.Value,
                frame,
                depth: 0.8f,
                sink: -0.05f,
                overshoot: 0.05f,
                maxEdgeLength: 0.5f);

            Assert.True(prismResult.IsSuccess);
            var topoResult = engine.Evaluators.ValidateTopology(prismResult.Value);
            Assert.True(topoResult.IsSuccess);
            Assert.True(topoResult.Value.IsManifold);
            Assert.True(topoResult.Value.IsWatertight);
            Assert.Equal(0, topoResult.Value.SelfIntersectionCount);
            Assert.False(topoResult.Value.HasDegenerateTriangles);
        });
    }
}
