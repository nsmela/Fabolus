using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Runs the detector over every model in the asset folder, with the band repair off and on.
///
/// <para>
/// The eight bodies the evaluation reports on are the ones the repair was built against, and a repair
/// tuned on the cases it was shown is worth exactly as much as its behaviour on the ones it was not.
/// The rest of the folder is the nearest thing available to that: raw bolus meshes and moulds at
/// assorted resolutions, several of them the un-smoothed originals of bodies in the eight. What is
/// being looked for is not quality - there is no reference for these - but movement: a body the repair
/// leaves alone should come back identical, and one it touches should come back with the same number
/// of closed contours it had before.
/// </para>
///
/// <para>Prints rather than asserts; a file the pipeline cannot open is reported and skipped.</para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class RidgeAllAssets
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public RidgeAllAssets(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [Fact]
    public void EveryModelInTheFolder()
    {
        var root = Path.GetDirectoryName(_assets.GetAssetPath("sphere.stl"))!;
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".stl", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f))
            .ToList();

        _log.WriteLine($"{files.Count} models under {root}");
        _log.WriteLine("");
        _log.WriteLine("  model                            faces  genus |  off: cont/closed  band  " +
                       "| on: cont/closed  band  grown | verdict");

        int moved = 0, held = 0, skipped = 0;

        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);

            var body = Body(file, out string? error);
            if (body is null)
            {
                _log.WriteLine($"  {name,-32} skipped: {error}");
                skipped++;
                continue;
            }

            var off = RidgeDetection.Diagnose(body, RidgeDetectionOptions.Default with
            {
                BandShortfallFraction = 0f,
            });
            var on = RidgeDetection.Diagnose(body, RidgeDetectionOptions.Default);

            int faces = body.Triangles.Length / 3;
            bool touched = on.RidgeFaces.Count(r => r) != off.RidgeFaces.Count(r => r);

            string verdict =
                !touched ? "untouched"
                : on.Contours.Count(c => c.IsClosed) < off.Contours.Count(c => c.IsClosed)
                    ? "REPAIRED, lost a closed contour"
                    : "repaired";

            if (touched) moved++;
            else held++;

            _log.WriteLine(
                $"  {name,-32} {faces,6} {off.Report.Surface.Genus,6} | " +
                $"{off.Contours.Count,6}/{off.Contours.Count(c => c.IsClosed),-6} " +
                $"{off.BandProfile.MedianWidth,5:F1} | " +
                $"{on.Contours.Count,4}/{on.Contours.Count(c => c.IsClosed),-6} " +
                $"{on.BandProfile.MedianWidth,5:F1} " +
                $"{on.RidgeFaces.Count(r => r) - off.RidgeFaces.Count(r => r),5} | {verdict}");
        }

        _log.WriteLine("");
        _log.WriteLine($"{held} untouched, {moved} repaired, {skipped} skipped");
    }

    /// <summary>
    /// The mesh the Parting Split scene runs detection on. Failure anywhere along the way is a fact
    /// about the file rather than about the repair, so it is reported and the file passed over.
    /// </summary>
    private IMesh? Body(string file, out string? error)
    {
        error = null;
        try
        {
            var imported = _engine.IO.Import(file);
            if (imported.IsFailure)
            {
                error = $"import - {imported.Error.Description}";
                return null;
            }

            var mould = MouldMesh.Create(imported.Value);
            if (mould.IsFailure)
            {
                error = $"mould - {mould.Error.Description}";
                return null;
            }

            var body = new PartingMeshFeature(_engine).GetBodyMesh(mould.Value);
            if (body.IsFailure)
            {
                error = $"body - {body.Error.Description}";
                return null;
            }

            return body.Value.Mesh;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name} - {ex.Message}";
            return null;
        }
    }
}
