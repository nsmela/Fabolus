using System.Numerics;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// The image set for one body. Three sheets, because three different questions get asked of the same
/// geometry and mixing them into one picture answers none of them:
/// <list type="bullet">
///   <item><b>plain</b> - what the body actually is, with nothing drawn on it. The baseline you need
///     before you can say a marking is in the wrong place.</item>
///   <item><b>ridge</b> - the fill and the crease facets in separate tints, plus the contours. The
///     two tints are the point: they say whether the rim came out as a filled band or as bare crease
///     facets with unshaded surface trapped between them.</item>
///   <item><b>vs-seam</b> - contours against the <c>ThicknessParting</c> seam, which is the line the
///     split actually cuts on today. Agreement or disagreement here is the whole question of which
///     mechanism should own the parting line.</item>
/// </list>
/// </summary>
internal static class RidgeImages
{
    private static readonly Rgb Background = new(24, 26, 32);
    private static readonly Rgb Plain = new(190, 193, 200);

    // Deliberately muted, and deliberately not any colour in the contour palette. The tints say where
    // the passes marked surface; the contours are what is being judged, and they have to read on top
    // of the tint rather than merging into it.
    private static readonly Rgb Band = new(95, 110, 170);      // filled by the region pass: a rim wall
    private static readonly Rgb Crease = new(180, 120, 60);    // touches a ridge edge but was not filled
    private static readonly Rgb Seam = new(64, 224, 208);
    private static readonly Rgb RidgeAgainstSeam = new(230, 70, 180);

    // The Parting Split scene's own palette, so the last sheet shows what the user will actually be
    // looking at rather than a diagnostic recolouring of it.
    private static readonly Rgb DraftAlong = new(255, 0, 0);
    private static readonly Rgb DraftAway = new(0, 255, 0);
    private static readonly Rgb DraftNeutral = new(204, 204, 204);
    private static readonly Rgb SceneRidgeRegion = new(94, 110, 171);

    private const int Full = 1024;
    private const int Tile = 460;

    // The disagreement map. Deliberately blunt colours - this sheet exists to be scanned for red and
    // blue, not admired.
    private static readonly Rgb AgreeBoth = new(95, 110, 170);
    private static readonly Rgb RidgeOnly = new(220, 60, 60);
    private static readonly Rgb ThicknessOnly = new(60, 130, 230);

    // Diverging about the median: cool where the band is narrower than usual, warm where it is wider,
    // near-neutral where it is doing what a rim wall does. Diverging rather than sequential because
    // both directions are faults - a pinch and a bulge are equally not-a-constant-width wall.
    private static readonly Rgb WidthNarrow = new(70, 140, 255);
    private static readonly Rgb WidthNormal = new(210, 210, 215);
    private static readonly Rgb WidthWide = new(255, 90, 60);

    private static readonly Rgb BayFill = new(255, 90, 60);

    public static void WriteAll(
        string directory, string model, IMesh body, RidgeDiagnosis diagnosis, PartingLine? seam,
        ThicknessReport thickness, IReadOnlyList<BandWidth> bandWidths, BayReport bays)
    {
        Directory.CreateDirectory(directory);

        var options = RenderOptions.Default;
        var contours = diagnosis.Contours;

        Rgb FaceColour(int face) =>
            diagnosis.FilledFaces[face] ? Band
            : diagnosis.RidgeFaces[face] ? Crease
            : Plain;

        // Exactly what PartingSplitSceneManager paints: draft classification per facet, with the ridge
        // region replacing it where the rim is. Reproduced here rather than trusted, because the scene
        // itself needs a live D3D device and cannot be rendered in a test.
        var draft = new ComputePartingDirectionColors()
            .Execute(body, new PartingLineParameters { PullDirection = Vector3.UnitY });

        Rgb SceneColour(int face)
        {
            if (diagnosis.RidgeFaces[face]) return SceneRidgeRegion;
            if (draft.IsFailure) return Plain;

            var c = draft.Value;
            return c[face * 3] > 0.9 ? DraftAlong
                : c[(face * 3) + 1] > 0.9 ? DraftAway
                : DraftNeutral;
        }

        // The threshold-free mask: faces whose probe never crossed the wall. Preferred over the wider
        // corridor for the picture, because anything it flags is flagged without a number having been
        // chosen, so a disagreement cannot be argued away as a badly set band.
        var agreement = thickness.Agreements.FirstOrDefault(a => a.Mask == "unmeasured");

        Rgb AgreementColour(int face) => agreement is null ? Plain : agreement.PerFace[face] switch
        {
            RidgeAgreementClass.Both => AgreeBoth,
            RidgeAgreementClass.RidgeOnly => RidgeOnly,
            RidgeAgreementClass.ThicknessOnly => ThicknessOnly,
            _ => Plain,
        };

        var plainTiles = new List<Tile>();
        var ridgeTiles = new List<Tile>();
        var seamTiles = new List<Tile>();
        var sceneTiles = new List<Tile>();
        var thicknessTiles = new List<Tile>();
        var widthTiles = new List<Tile>();
        var bayTiles = new List<Tile>();
        var suspectTiles = new List<Tile>();

        var profile = diagnosis.BandProfile;

        Rgb SuspectColour(int face)
        {
            if (!profile.Available || !diagnosis.RidgeFaces[face]) return Plain;
            if (profile.PerFaceSuspect[face]) return new Rgb(255, 70, 60);

            // Band shaded by how close it runs to what the band beside it is doing, so a stretch that
            // is merely thin reads differently from one that is suspect.
            float w = profile.PerFaceWidth[face];
            float e = profile.PerFaceExpected[face];
            if (float.IsPositiveInfinity(w) || float.IsPositiveInfinity(e) || e < 1e-6f) return Band;

            return Rgb.Lerp(new Rgb(150, 130, 60), Band, Math.Clamp((w / e - 0.5f) / 0.5f, 0f, 1f));
        }

        Rgb BayColour(int face) =>
            bays.Available && bays.PerFace[face] ? BayFill
            : diagnosis.RidgeFaces[face] ? Band
            : Plain;

        var paired = bandWidths.Where(w => w.Paired && w.PerPoint.Length > 0).ToList();

        // Ratio to that contour's own median, so a thick body and a thin one are read on the same
        // scale and the picture is about evenness rather than about size.
        Rgb WidthColour(BandWidth w, int point)
        {
            float ratio = w.Median > 1e-6f ? w.PerPoint[point] / w.Median : 1f;
            return ratio < 1f
                ? Rgb.Lerp(WidthNarrow, WidthNormal, Math.Clamp((ratio - 0.5f) / 0.5f, 0f, 1f))
                : Rgb.Lerp(WidthNormal, WidthWide, Math.Clamp(ratio - 1f, 0f, 1f));
        }

        foreach (var view in Views.Standard)
        {
            // Tiles are rendered at their own size rather than downscaled from the full view: a
            // 2px contour survives rendering small far better than it survives being resampled.
            plainTiles.Add(new Tile(view.Name,
                MeshRasterizer.Render(body, Camera.Fit(body, view, Tile, Tile), Tile, Tile, options)));

            // Full size and unmarked. When a marking looks wrong the first question is always what the
            // body actually does there, and every other sheet has something painted over the answer.
            MeshRasterizer.Render(body, Camera.Fit(body, view, Full, Full), Full, Full, options)
                .Save(Path.Combine(directory, $"plain-{view.Name}.png"));

            ridgeTiles.Add(new Tile(view.Name, Draw(body, view, Tile, contours, options, FaceColour, null, null)));

            // One colour for every ridge contour here: this sheet asks whether the ridge agrees with
            // the seam, and per-contour colours would only compete with the two-colour comparison.
            seamTiles.Add(new Tile(view.Name,
                Draw(body, view, Tile, contours, options, null, seam, RidgeAgainstSeam)));

            sceneTiles.Add(new Tile(view.Name,
                Draw(body, view, Tile, contours, options, SceneColour, null, new Rgb(198, 76, 255))));

            // No contours drawn here: the question is which faces the two measurements disagree about,
            // and a curve over the top only hides the answer.
            if (agreement is not null)
                thicknessTiles.Add(new Tile(view.Name,
                    MeshRasterizer.Render(
                        body, Camera.Fit(body, view, Tile, Tile), Tile, Tile, options, AgreementColour)));

            if (profile.Available)
                suspectTiles.Add(new Tile(view.Name,
                    MeshRasterizer.Render(
                        body, Camera.Fit(body, view, Tile, Tile), Tile, Tile, options, SuspectColour)));

            if (bays.Available && bays.Count > 0)
                bayTiles.Add(new Tile(view.Name,
                    MeshRasterizer.Render(
                        body, Camera.Fit(body, view, Tile, Tile), Tile, Tile, options, BayColour)));

            if (paired.Count > 0)
            {
                // Plain body underneath: the measurement is on the curves, and tinting the faces as
                // well would only compete with it.
                var camera = Camera.Fit(body, view, Tile, Tile);
                var image = MeshRasterizer.Render(body, camera, Tile, Tile, options);

                foreach (var w in paired)
                    MeshRasterizer.DrawPolyline(
                        image, camera, contours[w.ContourIndex].Points, contours[w.ContourIndex].IsClosed,
                        point => WidthColour(w, point), options);

                widthTiles.Add(new Tile(view.Name, image));

                var full = Camera.Fit(body, view, Full, Full);
                var large = MeshRasterizer.Render(body, full, Full, Full, options);
                foreach (var w in paired)
                    MeshRasterizer.DrawPolyline(
                        large, full, contours[w.ContourIndex].Points, contours[w.ContourIndex].IsClosed,
                        point => WidthColour(w, point), options);
                large.Save(Path.Combine(directory, $"width-{view.Name}.png"));
            }

            // Full size, ridge only - this is what gets opened when a tile shows something odd.
            Draw(body, view, Full, contours, options, FaceColour, null, null)
                .Save(Path.Combine(directory, $"view-{view.Name}.png"));
        }

        ContactSheet.Save(Path.Combine(directory, "sheet-plain.png"), plainTiles, 4, Tile, Background,
            $"{model} — body, unmarked", Array.Empty<(string, Rgb)>());

        ContactSheet.Save(Path.Combine(directory, "sheet.png"), ridgeTiles, 4, Tile, Background,
            $"{model} — ridge detection ({contours.Count} contours)",
            new[]
            {
                ("filled band", Band),
                ("crease facets only", Crease),
                ("contour", MeshRasterizer.ContourColour(0)),
            });

        ContactSheet.Save(Path.Combine(directory, "sheet-vs-seam.png"), seamTiles, 4, Tile, Background,
            $"{model} — ridge contours vs ThicknessParting seam",
            new[] { ("ridge contour", RidgeAgainstSeam), ("seam", Seam) });

        if (suspectTiles.Count > 0)
            ContactSheet.Save(Path.Combine(directory, "sheet-suspect.png"), suspectTiles, 4, Tile, Background,
                $"{model} — band width vs the band beside it " +
                $"({profile.SuspectFaces} suspect faces, {profile.SuspectAreaFraction:P1} of band, " +
                $"median {profile.MedianWidth:F1} mm)",
                new[]
                {
                    ("suspect (<0.5x local)", new Rgb(255, 70, 60)),
                    ("thin", new Rgb(150, 130, 60)),
                    ("as expected", Band),
                });

        if (bayTiles.Count > 0)
            ContactSheet.Save(Path.Combine(directory, "sheet-bays.png"), bayTiles, 4, Tile, Background,
                $"{model} — surface reaching into the band ({bays.Count} bays, " +
                $"{bays.BayAreaFraction:P1} of band area, radius {bays.RadiusMm:F1} mm)",
                new[] { ("band", Band), ("bay", BayFill), ("surface", Plain) });

        if (widthTiles.Count > 0)
        {
            float worst = paired.Max(w => w.CoefficientOfVariation);
            ContactSheet.Save(Path.Combine(directory, "sheet-band-width.png"), widthTiles, 4, Tile, Background,
                $"{model} — rim band width, relative to each contour's median (worst CoV {worst:F3})",
                new[]
                {
                    ("≤0.5× median (pinched)", WidthNarrow),
                    ("at median", WidthNormal),
                    ("≥2× median (bulged)", WidthWide),
                });
        }

        if (thicknessTiles.Count > 0)
            ContactSheet.Save(Path.Combine(directory, "sheet-vs-thickness.png"), thicknessTiles, 4, Tile, Background,
                $"{model} — ridge band vs rim by wall thickness (IoU {agreement!.IoU:F2})",
                new[]
                {
                    ("both agree", AgreeBoth),
                    ("band only", RidgeOnly),
                    ("thickness only", ThicknessOnly),
                    ("neither", Plain),
                });

        ContactSheet.Save(Path.Combine(directory, "sheet-as-scene.png"), sceneTiles, 4, Tile, Background,
            $"{model} — as the Parting Split scene shades it (pull +Y)",
            new[]
            {
                ("along pull", DraftAlong),
                ("away", DraftAway),
                ("neutral", DraftNeutral),
                ("ridge region", SceneRidgeRegion),
                ("ridge contour", new Rgb(198, 76, 255)),
            });
    }

    private static Raster Draw(
        IMesh body, View view, int size, IReadOnlyList<RidgeContour> contours,
        RenderOptions options, Func<int, Rgb>? faceColour, PartingLine? seam, Rgb? uniform)
    {
        var camera = Camera.Fit(body, view, size, size);
        var image = MeshRasterizer.Render(body, camera, size, size, options, faceColour);

        if (seam is not null)
            foreach (var loop in seam.Loops)
                MeshRasterizer.DrawPolyline(image, camera, loop, closed: true, Seam, options);

        for (int i = 0; i < contours.Count; i++)
        {
            var colour = uniform ?? MeshRasterizer.ContourColour(i);
            MeshRasterizer.DrawPolyline(image, camera, contours[i].Points, contours[i].IsClosed, colour, options);

            // Where an open contour stops is the single most useful thing in the frame, so it gets a
            // marker rather than being left as a line that simply runs out.
            if (contours[i].IsClosed || contours[i].Points.Count < 2) continue;
            MeshRasterizer.DrawMarker(image, camera, contours[i].Points[0], new Rgb(255, 60, 60), options);
            MeshRasterizer.DrawMarker(image, camera, contours[i].Points[^1], new Rgb(255, 60, 60), options);
        }

        return image;
    }
}
