using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.Smoothing;
using Fabolus.Core.Features.Transforms;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// How many disconnected pieces the app's parting cut leaves, and how big each is. A separated
/// export writes one file per piece, so this is the file count a user gets (issue #88).
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class PartingPieceCount
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public PartingPieceCount(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [DiagnosticFact]
    public void PiecesOfTheAppsCut()
    {
        foreach (string file in new[] { "flat_vulva.stl", "scalp_bolus.stl", "chin_bolus.stl" })
            Report(file, MouldShapeType.Concave, loft: true, PartingMeshParameters.DefaultDepth, Vector3.UnitY, detail: true);
    }

    /// <summary>The same cut on flat_vulva across the settings a user can change.</summary>
    [DiagnosticFact]
    public void FlatVulvaAcrossSettings()
    {
        foreach (var shape in Enum.GetValues<MouldShapeType>())
        foreach (bool loft in new[] { true, false })
        foreach (double depth in new[] { 0.1, 1.0, 3.0 })
        foreach (var pull in new[] { Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX })
            Report("flat_vulva.stl", shape, loft, depth, pull, detail: false);
    }

    /// <summary>flat_vulva after the steps a user takes before the split: smoothing and air channels.</summary>
    [DiagnosticFact]
    public void FlatVulvaAfterTheWorkflow()
    {
        foreach (bool smooth in new[] { false, true })
        foreach (bool vents in new[] { false, true })
        foreach (var shape in Enum.GetValues<MouldShapeType>())
        {
            var (workspace, bodyId) = GeometryEngineFixture.AddBody(Workspace.CreateEmpty(), _assets.LoadStl("flat_vulva.stl"));
            if (smooth)
            {
                var smoothed = new SmoothMesh(_engine).Execute(workspace, new SmoothSettings());
                if (smoothed.IsFailure) { _log.WriteLine($"smooth failed: {smoothed.Error.Description}"); return; }
                workspace = smoothed.Value;
            }

            var bolus = workspace.GetMesh(bodyId).Value;
            AirChannelModel[] channels = [];
            if (vents)
            {
                var top = _engine.Evaluators.GetStatistics(bolus).Value.BoundsMax.Z;
                var pockets = new DetectAirPockets(_engine).Execute(bolus, new AirPocketSettings(1.0, 6.0), []);
                channels = pockets.IsSuccess
                    ? [.. pockets.Value.Take(8).Select(pocket => new AirChannelModel(
                        Guid.NewGuid(), AirChannelType.Straight, 3.0f, 4.0f, 3.0f,
                        new StraightAirChannel(pocket.Point, 3.0f, (float)(top - pocket.Point.Z + 10), 3.0f, 4.0f, 1.0f)))]
                    : [];
            }

            Report($"flat_vulva smooth={smooth} vents={channels.Length} {shape}", workspace, bodyId,
                MouldDefinition.OfShape(shape, 2.5, 5.0) with { AirChannels = channels },
                loft: true, PartingMeshParameters.DefaultDepth, Vector3.UnitY, detail: false);
        }
    }

    /// <summary>flat_vulva opened the way the app opens it, then turned as the rotate view would.</summary>
    [DiagnosticFact]
    public void FlatVulvaImportedAndRotated()
    {
        var turns = new (string Name, float Angle, Vector3 Axis)[]
        {
            ("none", 0f, Vector3.UnitX),
            ("90 about X", MathF.PI / 2, Vector3.UnitX),
            ("90 about Y", MathF.PI / 2, Vector3.UnitY),
            ("180 about X", MathF.PI, Vector3.UnitX),
            ("45 about X", MathF.PI / 4, Vector3.UnitX),
        };

        foreach (var (name, angle, axis) in turns)
        foreach (var shape in Enum.GetValues<MouldShapeType>())
        {
            var opened = new ImportMesh(_engine).Execute(Workspace.CreateEmpty(), _assets.GetAssetPath("flat_vulva.stl"));
            if (opened.IsFailure) { _log.WriteLine($"import failed: {opened.Error.Description}"); return; }
            var workspace = opened.Value;
            var bodyId = workspace.ActiveMeshId;
            if (name == "none")
                _log.WriteLine($"imported as {workspace.Records.Count} entr(y/ies)");

            if (angle != 0f)
            {
                var turned = new TransformMesh(_engine).Rotate(workspace, bodyId, angle, axis);
                if (turned.IsFailure) { _log.WriteLine($"rotate failed: {turned.Error.Description}"); continue; }
                workspace = turned.Value;
            }

            Report($"flat_vulva imported, rotate {name}, {shape}", workspace, bodyId,
                MouldDefinition.OfShape(shape, 2.5, 5.0),
                loft: true, PartingMeshParameters.DefaultDepth, Vector3.UnitY, detail: false);
        }
    }

    /// <summary>How far flat_vulva can be tilted before the cut stops severing it.</summary>
    [DiagnosticFact]
    public void FlatVulvaTiltSweep()
    {
        foreach (var axis in new[] { Vector3.UnitX, Vector3.UnitY })
        foreach (int degrees in new[] { 5, 10, 15, 20, 30, 45, 60, 75 })
        {
            var opened = new ImportMesh(_engine).Execute(Workspace.CreateEmpty(), _assets.GetAssetPath("flat_vulva.stl"));
            var bodyId = opened.Value.ActiveMeshId;
            var turned = new TransformMesh(_engine).Rotate(opened.Value, bodyId, degrees * MathF.PI / 180f, axis);
            if (turned.IsFailure) { _log.WriteLine($"rotate failed: {turned.Error.Description}"); continue; }

            Report($"tilt {degrees} about ({axis.X},{axis.Y},{axis.Z})", turned.Value, bodyId,
                MouldDefinition.OfShape(MouldShapeType.Concave, 2.5, 5.0),
                loft: true, PartingMeshParameters.DefaultDepth, Vector3.UnitY, detail: false, summaryOnly: true);
        }
    }

    private void Report(string file, MouldShapeType shape, bool loft, double depth, Vector3 pull, bool detail)
    {
        var (workspace, bodyId) = GeometryEngineFixture.AddBody(Workspace.CreateEmpty(), _assets.LoadStl(file));
        Report($"{file} {shape} loft={loft} depth={depth} pull=({pull.X},{pull.Y},{pull.Z})", workspace, bodyId,
            MouldDefinition.OfShape(shape, 2.5, 5.0), loft, depth, pull, detail);
    }

    private void Report(string label, Workspace workspace, Guid bodyId, MouldDefinition definition,
        bool loft, double depth, Vector3 pull, bool detail, bool summaryOnly = false)
    {
        var mould = new GenerateMould(_engine).Execute(workspace, bodyId, definition with { TargetMeshId = bodyId });
        if (mould.IsFailure) { _log.WriteLine($"{label}: mould failed: {mould.Error.Description}"); return; }

        // The recipe PartingSplitViewModel fixes for new work.
        var line = PartingLineParameters.Default with
        {
            Source = PartingLineSource.ExtrusionBorder,
            PullDirection = pull,
            NeutralBand = PartingNeutralBand.FromDegrees(-5.0, 5.0),
        };
        var parameters = PartingMeshParameters.Default with
        {
            AxisSource = PartingMeshAxisSource.PartingLine,
            Sweep = loft ? PartingMeshSweep.MouldLoft : PartingMeshSweep.SurfaceSweep,
            SplitMethod = PartingSplitMethod.SeveredComponents,
            Thickening = PartingMeshThickening.Extrude,
            Depth = depth,
            FlangeMaxSlopeDeg = 80.0,
            NormalFollowMm = 100.0,
            RawFlange = false,
            NormalSmoothingPasses = 5,
            Axis = pull,
        };

        var cut = new SplitMouldFeature(_engine).ExecuteCut(
            mould.Value, bodyId, line, parameters, PartingResultMode.Separated);
        if (cut.IsFailure) { _log.WriteLine($"{label}: cut failed: {cut.Error.Description}"); return; }

        var piece = cut.Value.Records.Single(r => r.Id != bodyId);
        var pieces = _engine.Evaluators.SeparateComponents(cut.Value.GetMesh(piece.Id).Value).Value
            .Select(p => (Mesh: p, Volume: Volume(p)))
            .OrderByDescending(p => p.Volume)
            .ToList();
        double total = pieces.Sum(p => p.Volume);

        _log.WriteLine($"{label}: {pieces.Count} pieces, {total / 1000.0:F1} mL, " +
            $"split {string.Join(" / ", pieces.Take(3).Select(p => (p.Volume / total).ToString("P1")))}");
        if (summaryOnly || (!detail && pieces.Count <= 2)) return;

        foreach (var (mesh, volume) in pieces)
        {
            var v = mesh.Vertices;
            var min = new Vector3(v.Min(p => p.X), v.Min(p => p.Y), v.Min(p => p.Z));
            var max = new Vector3(v.Max(p => p.X), v.Max(p => p.Y), v.Max(p => p.Z));
            var c = (min + max) * 0.5;
            _log.WriteLine(
                $"    {volume,10:F1} mm3 {volume / total,7:P2}  tris {mesh.Triangles.Length / 3,6}  " +
                $"size {max.X - min.X:F1} x {max.Y - min.Y:F1} x {max.Z - min.Z:F1}  " +
                $"centre ({c.X:F1}, {c.Y:F1}, {c.Z:F1})");
        }
    }

    private static double Volume(IMesh mesh)
    {
        var v = mesh.Vertices;
        var t = mesh.Triangles;
        double total = 0;
        for (int i = 0; i + 2 < t.Length; i += 3)
            total += v[t[i]].Dot(v[t[i + 1]].Cross(v[t[i + 2]])) / 6.0;
        return Math.Abs(total);
    }
}
