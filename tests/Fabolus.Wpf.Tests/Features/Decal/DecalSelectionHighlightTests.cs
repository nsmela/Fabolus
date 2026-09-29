using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Features.Decal;
using HelixToolkit.Wpf.SharpDX;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.Decal;

/// <summary>
/// How the viewport shows which decal is selected: the selected one keeps its full emboss or
/// engrave colour and the rest are muted, so the live decal is the only one at full strength.
/// </summary>
/// <remarks>
/// Before this, every decal of the same operation rendered identically and the whole selection
/// signal was one 1.5px line - which a curved surface could also swallow, since the box carried
/// no depth bias while the decal prisms did.
/// </remarks>
public class DecalSelectionHighlightTests
{
    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    [Fact]
    public void TheSelectedDecalIsTheBrightestOfThem()
    {
        var scene = NewScene(out var decals);
        var skins = SkinsAfterSelecting(scene, decals, decals[0].Id);

        Assert.Equal(3, skins.Count);

        var selected = skins[decals[0].Id];
        var others = decals.Skip(1).Select(d => skins[d.Id]).ToList();

        Assert.All(others, other => Assert.True(
            Alpha(other) < Alpha(selected),
            $"an unselected decal is at alpha {Alpha(other):F2}, not below the selected decal's {Alpha(selected):F2}"));
    }

    /// <summary>
    /// The selected decal glows a little, so it stays the brightest thing on screen even when it
    /// is turned away from the light. Diffuse colour alone cannot do that: the same decal is
    /// bright on the lit side of a bolus and dark on the shadowed side.
    /// </summary>
    [Fact]
    public void OnlyTheSelectedDecalIsLitFromWithin()
    {
        var scene = NewScene(out var decals);
        var skins = SkinsAfterSelecting(scene, decals, decals[0].Id);

        Assert.True(Glow(skins[decals[0].Id]) > 0f, "the selected decal has no emissive lift at all");
        Assert.All(decals.Skip(1), d => Assert.Equal(0f, Glow(skins[d.Id])));
    }

    /// <summary>
    /// Two decals of the same operation used to be indistinguishable. This is the case the change
    /// is actually for.
    /// </summary>
    [Fact]
    public void TwoEmbossDecals_AreToldApartBySelection()
    {
        var scene = NewScene(out var decals);

        var first = SkinsAfterSelecting(scene, decals, decals[0].Id);
        Assert.NotEqual(Alpha(first[decals[0].Id]), Alpha(first[decals[1].Id]));

        // Selecting the other one swaps which is at full strength rather than leaving both muted
        // or both full.
        var second = SkinsAfterSelecting(scene, decals, decals[1].Id);
        Assert.Equal(Alpha(first[decals[0].Id]), Alpha(second[decals[1].Id]));
        Assert.Equal(Alpha(first[decals[1].Id]), Alpha(second[decals[0].Id]));
    }

    /// <summary>
    /// Nothing selected means nothing to point at, so no decal is singled out either way: none is
    /// brightened, and none is held back. Muting the whole set would only repeat what the empty
    /// side panel already says, and brightening them all would say something false.
    /// </summary>
    [Fact]
    public void WithNothingSelected_NoDecalIsSingledOut()
    {
        var scene = NewScene(out var decals);

        var withSelection = SkinsAfterSelecting(scene, decals, decals[0].Id);
        var none = SkinsAfterSelecting(scene, decals, Guid.Empty);

        // Emboss and engrave differ in hue, not in alpha, so one value covers all three.
        var neutral = Alpha(none[decals[0].Id]);
        Assert.All(decals, d => Assert.Equal(neutral, Alpha(none[d.Id])));
        Assert.All(decals, d => Assert.Equal(0f, Glow(none[d.Id])));

        // Neutral is its own state: neither the brightened nor the muted one.
        Assert.True(neutral < Alpha(withSelection[decals[0].Id]),
            $"neutral alpha {neutral:F2} is not below the selected decal's {Alpha(withSelection[decals[0].Id]):F2}");
        Assert.True(neutral > Alpha(withSelection[decals[1].Id]),
            $"neutral alpha {neutral:F2} is not above a muted decal's {Alpha(withSelection[decals[1].Id]):F2}");
    }

    /// <summary>
    /// The selection box is a flat rectangle a fixed distance along the decal's normal, so over a
    /// curved bolus its corners lean into the surface. Without the same depth bias the prisms
    /// carry, the mesh wins and the marker breaks up right where the surface curves most.
    /// </summary>
    [Fact]
    public void TheSelectionBoxIsBiasedClearOfTheSurface()
    {
        var scene = NewScene(out var decals);

        var lines = new List<LineGeometryModel3D>();
        scene.VisualAddedOrUpdated += v => { if (v is LineGeometryModel3D l) lines.Add(l); };

        Refresh(scene, decals, decals[0].Id);

        var box = Assert.Single(lines, l => l.Color == System.Windows.Media.Colors.Cyan);
        Assert.True(box.DepthBias < 0, $"selection box DepthBias is {box.DepthBias}, so the surface can hide it");
        Assert.True(box.SlopeScaledDepthBias < 0f,
            $"selection box SlopeScaledDepthBias is {box.SlopeScaledDepthBias}, so it still breaks up on a slope");
    }

    private static float Alpha(Material skin) =>
        Assert.IsType<PhongMaterial>(skin).DiffuseColor.Alpha;

    /// <summary>How much the skin lights itself, independently of the scene's lights.</summary>
    private static float Glow(Material skin)
    {
        var emissive = Assert.IsType<PhongMaterial>(skin).EmissiveColor;
        return emissive.Red + emissive.Green + emissive.Blue;
    }

    /// <summary>
    /// The skin each decal is wearing once the scene has been refreshed with
    /// <paramref name="selectedId"/> selected.
    /// </summary>
    /// <remarks>
    /// Refreshed one decal at a time. The scene emits visuals keyed by their own GUID and keeps
    /// the visual-to-decal mapping private, so rendering a single-decal list is what lets the
    /// material that comes back be attributed to the decal that produced it. Selection is passed
    /// through unchanged, so a decal renders muted here exactly when it would in the full set.
    /// </remarks>
    private static Dictionary<Guid, Material> SkinsAfterSelecting(
        DecalSceneManager scene, List<TextDecal> decals, Guid selectedId)
    {
        var skins = new Dictionary<Guid, Material>();

        foreach (var decal in decals)
        {
            Material? captured = null;
            void Capture(Element3D visual)
            {
                if (visual is MeshGeometryModel3D mesh && mesh.Material is not null) captured = mesh.Material;
            }

            scene.ClearPreviewVisuals();
            scene.VisualAddedOrUpdated += Capture;
            Refresh(scene, [decal], selectedId);
            scene.VisualAddedOrUpdated -= Capture;

            Assert.NotNull(captured);
            skins[decal.Id] = captured!;
        }

        return skins;
    }

    private static void Refresh(DecalSceneManager scene, IReadOnlyList<TextDecal> decals, Guid selectedId) =>
        scene.UpdateDecals(decals, selectedId, new WpfGlyphOutlineSource(), EmbossTarget.Base);

    private static DecalSceneManager NewScene(out List<TextDecal> decals)
    {
        var scene = new DecalSceneManager(Engine, new StrongReferenceMessenger());

        var mesh = Engine.Generators.GenerateBox(new Vector3(-40, -40, 0), new Vector3(40, 40, 20)).Value
            .WithMeasurements(Engine);
        Assert.True(scene.UpdateMesh(mesh).IsSuccess);

        // Three on the top face: two embossed, so the same-operation case is covered, and one
        // engraved so the muting is exercised on both colours.
        decals = [
            DecalAt("ONE", EmbossOperation.Emboss, -20f),
            DecalAt("TWO", EmbossOperation.Emboss, 0f),
            DecalAt("THREE", EmbossOperation.Engrave, 20f),
        ];

        return scene;
    }

    private static TextDecal DecalAt(string text, EmbossOperation operation, float x) =>
        new() {
            Id = Guid.NewGuid(),
            Text = text,
            Operation = operation,
            Target = EmbossTarget.Base,
            Anchor = new Vector3(x, 0f, 20f),
            AnchorNormal = Vector3.UnitZ,
        };
}
