using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using BasicResults;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.Overhangs;
using Fabolus.Core.Features.Smoothing;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using GeometryEngine;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Benchmarks;

/// <summary>
/// What a user waits for through one session on a scan - open it, smooth it, look at its
/// overhangs, find its air pockets, build a mould, adjust a channel and build it again, save it -
/// timed step by step on real bolus scans, through the same feature classes the app calls.
///
/// It exists to answer one question about a change to GeometryEngine: what does it do to the
/// app? The engine's own benchmarks time its operations; this times the sequences Fabolus makes
/// of them, where a step's cost depends on what the steps before it left behind. Build it against
/// two checkouts of the engine and compare what it prints:
///
///   dotnet test tests/Fabolus.Core.Tests -c Release -p:GeometryEngineRoot=C:\path\to\engine\
///
/// It is written against the engine's oldest API in use here and nothing newer, so it compiles
/// against either checkout unchanged. Whether the engine under test keeps native solids, or can
/// describe a boolean before running it, is found by looking, and printed.
///
/// Environment: FABOLUS_BENCH=1 to run at all. FABOLUS_BENCH_SCANS, a comma-separated list of
/// file names, to run other scans than the four below. FABOLUS_BENCH_LABEL, a name for the engine
/// under test, printed in the header. FABOLUS_BENCH_OUT, a file to append everything printed to.
/// </summary>
public sealed class WorkflowBenchmarks(ITestOutputHelper output)
{
    /// <summary>How many times an adjust-and-rebuild is repeated; the middle time is reported.</summary>
    private const int Adjustments = 5;

    /// <summary>The app's defaults: the mould view's tip, the print bed's channel and pockets.</summary>
    private const float TipLength = 3.0f;
    private const float TipDiameter = 3.0f;
    private const float TipDepth = 1.0f;
    private const float ChannelDiameter = 4.0f;
    private const double PocketDepth = 1.0;
    private const double ChannelSpacing = 6.0;

    /// <summary>A mould takes as many channels as the scan has pockets, up to this many.</summary>
    private const int MostChannels = 8;

    /// <summary>
    /// A small, a middling and two large scans, the same four the view benchmarks open, so a cost
    /// that grows with the mesh shows as a trend.
    /// </summary>
    private static readonly string[] Scans =
    [
        "eye_bolus.stl",
        "chin_bolus.stl",
        "scalp_bolus.stl",
        "test_smoothed_bolus.stl",
    ];

    private readonly StringBuilder _printed = new();

    [BenchmarkFact]
    public void A_session_on_each_scan()
    {
        // The same factory the app uses, so this is the kernel that ships, set up as it ships.
        var engine = BspGeometryEngine.Create();
        DescribeRun(engine);

        var scans = Environment.GetEnvironmentVariable("FABOLUS_BENCH_SCANS") is { Length: > 0 } chosen
            ? chosen.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Scans;

        var whole = Stopwatch.StartNew();
        foreach (var scan in scans)
        {
            Session(engine, scan);
        }

        Print("");
        Print($"every session: {whole.Elapsed.TotalMilliseconds,10:N0} ms");
        DescribeMemory();

        if (Environment.GetEnvironmentVariable("FABOLUS_BENCH_OUT") is { Length: > 0 } file)
        {
            File.AppendAllText(file, _printed.ToString());
        }
    }

    private void Session(IGeometryEngine engine, string scan)
    {
        var steps = new List<Step>();
        var session = Stopwatch.StartNew();
        var workspace = Workspace.CreateEmpty();

        // Open the scan.
        var opened = Time(steps, "open the scan", () => new ImportMesh(engine).Execute(workspace, AssetPath(scan)));
        if (opened.IsFailure)
        {
            Report(scan, null, steps, session);
            return;
        }

        workspace = opened.Value;
        var scanTriangles = workspace.GetActiveMesh().Value.TriangleCount;

        // Smooth it, with the settings the smoothing view opens on. A scan too thin to survive
        // them is carried on unsmoothed, so the rest of its session is still timed.
        var smoothed = Time(steps, "smooth it", () => new SmoothMesh(engine).Execute(workspace, new SmoothSettings()));
        workspace = smoothed.IsSuccess ? smoothed.Value : workspace;

        var bolusId = workspace.ActiveMeshId;
        var bolus = workspace.GetActiveMesh().Value;
        var bounds = engine.Evaluators.GetStatistics(bolus).Value;

        // Colour its overhangs, as the rotate view does while the user turns it.
        Time(steps, "colour its overhangs", () => new ComputeOverhangColors(engine).Execute(
            bolus, new OverhangSettings(OverhangDirection.MouldDefault, ColourGradient.Overhang)));

        // Find where air would be trapped, and stand a channel on each pocket.
        var pockets = Time(steps, "find its air pockets", () =>
            new DetectAirPockets(engine).Execute(bolus, new AirPocketSettings(PocketDepth, ChannelSpacing), []));

        var vents = pockets.IsSuccess && pockets.Value.Count > 0
            ? pockets.Value.Take(MostChannels).Select(pocket => pocket.Point).ToList()
            : [new Vector3((bounds.BoundsMin.X + bounds.BoundsMax.X) / 2, (bounds.BoundsMin.Y + bounds.BoundsMax.Y) / 2, bounds.BoundsMax.Z)];

        // Build each kind of mould the way the view previews it: from the bolus, every time.
        // Then adjust one channel and build it again, which is what a user does most - and the
        // bolus is the same mesh each time, which is where an engine that remembers a mesh it
        // has already read would show it.
        MouldDefinition[] kinds = [new ConvexMouldDefinition(), new ContouredMouldDefinition()];
        foreach (var kind in kinds)
        {
            var name = kind is ConvexMouldDefinition ? "convex" : "contoured";
            Time(steps, $"build a {name} mould", () => Build(engine, bolus, kind, Channels(vents, bounds, nudge: 0)));

            var rebuilds = new List<Step>();
            for (var adjustment = 1; adjustment <= Adjustments; adjustment++)
            {
                var channels = Channels(vents, bounds, nudge: adjustment);
                Time(rebuilds, "rebuild", () => Build(engine, bolus, kind, channels));
            }

            steps.Add(Step.MiddleOf($"  move a channel, rebuild (x{Adjustments})", rebuilds));
        }

        // Commit the mould to the workspace, as Generate does, then save it both ways and open
        // the saved package again.
        var committed = Time(steps, "commit the convex mould", () => new GenerateMould(engine).Execute(
            workspace, bolusId, new ConvexMouldDefinition { AirChannels = Channels(vents, bounds, nudge: 0) }));

        if (committed.IsSuccess)
        {
            var mould = committed.Value.GetActiveMesh().Value;
            var record = committed.Value.GetActiveRecord().Value;
            var folder = Directory.CreateTempSubdirectory("fabolus-bench-").FullName;
            try
            {
                var (stl, package) = (Path.Combine(folder, "mould.stl"), Path.Combine(folder, "mould.3mf"));
                Time(steps, "save the mould as STL", () => new ExportMesh(engine).Execute(mould, record, stl, overwrite: true));
                Time(steps, "save the mould as a package", () => new ExportMesh(engine).Execute(mould, record, package, overwrite: true));
                Time(steps, "open the package again", () => new ImportMesh(engine).Execute(Workspace.CreateEmpty(), package));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        Report(scan, $"{scanTriangles:N0} triangles as scanned, {bolus.TriangleCount:N0} as smoothed, {vents.Count} channel(s)", steps, session);
    }

    /// <summary>What the mould view builds for a preview: the mould, measured, ready to show.</summary>
    private static Result<IMesh> Build(IGeometryEngine engine, IMesh bolus, MouldDefinition kind, AirChannelModel[] channels)
    {
        var built = (kind with { AirChannels = channels }).Apply(engine, bolus);
        return built.IsSuccess ? Result.Success(built.Value.Measured(engine)) : built;
    }

    /// <summary>
    /// A straight channel on each vent, rising clear of the mould. A nudge moves the first one a
    /// little further along x each time, as dragging it would: a different mould every time, on
    /// the same bolus.
    /// </summary>
    private static AirChannelModel[] Channels(IReadOnlyList<Vector3> vents, MeshStatistics bounds, int nudge) =>
        [.. vents.Select((vent, index) =>
        {
            var foot = index == 0 ? vent + new Vector3(0.4 * nudge, 0, 0) : vent;
            var length = (float)(bounds.BoundsMax.Z - foot.Z + 10);
            return new AirChannelModel(
                Guid.NewGuid(),
                AirChannelType.Straight,
                TipDiameter,
                ChannelDiameter,
                TipLength,
                new StraightAirChannel(foot, TipLength, length, TipDiameter, ChannelDiameter, TipDepth));
        })];

    private static Result<T> Time<T>(List<Step> steps, string label, Func<Result<T>> work)
    {
        var watch = Stopwatch.StartNew();
        var result = work();
        steps.Add(new Step(label, watch.Elapsed.TotalMilliseconds, result.IsSuccess ? null : result.Error.Code));
        return result;
    }

    private static void Time(List<Step> steps, string label, Func<Result> work)
    {
        var watch = Stopwatch.StartNew();
        var result = work();
        steps.Add(new Step(label, watch.Elapsed.TotalMilliseconds, result.IsSuccess ? null : result.Error.Code));
    }

    private void Report(string scan, string? about, List<Step> steps, Stopwatch session)
    {
        Print("");
        Print($"{scan}{(about is null ? "" : $": {about}")}");
        foreach (var step in steps)
        {
            Print($"  {step.Label,-36} {step.Milliseconds,10:N1} ms{(step.Failure is null ? "" : $"   FAILED: {step.Failure}")}");
        }

        Print($"  {"the whole session",-36} {session.Elapsed.TotalMilliseconds,10:N1} ms");
    }

    private void DescribeRun(IGeometryEngine engine)
    {
        var assembly = typeof(BspGeometryEngine).Assembly;
        var core = typeof(IMesh).Assembly;

        Print($"Fabolus workflow benchmark, {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        Print($"engine under test: {Environment.GetEnvironmentVariable("FABOLUS_BENCH_LABEL") ?? "(no FABOLUS_BENCH_LABEL given)"}");
        Print($"  {assembly.GetName().Name} {assembly.GetName().Version}, built {File.GetLastWriteTime(assembly.Location).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        Print($"  keeps native solids with their meshes: {(assembly.GetType("GeometryEngine.SolidRetention") is null ? "no" : "yes")}");
        Print($"  can describe a boolean before running it: {(core.GetType("GeometryEngine.Core.Geometry.Solid") is null ? "no" : "yes")}");
        Print($"  native kernel behind a boolean: {KernelOf(engine)}");
        Print($"{RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription}, {Environment.ProcessorCount} logical processors");
        Print("The first scan also pays for everything being compiled; read trends from the later ones.");
    }

    /// <summary>Which kernel a boolean actually runs on here, read off a result's own record.</summary>
    private static string KernelOf(IGeometryEngine engine)
    {
        var a = engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 5);
        var b = engine.Generators.GenerateSphere(new Vector3(3, 0, 0), 5);
        var cut = a.IsSuccess && b.IsSuccess ? engine.Booleans.Subtract(a.Value, b.Value) : Result.Failure<IMesh>(a.IsFailure ? a.Error : b.Error);
        return cut.IsSuccess ? cut.Value.Metadata.CreatedBy : $"none ({cut.Error.Code})";
    }

    private void DescribeMemory()
    {
        using var process = Process.GetCurrentProcess();
        const double Megabyte = 1024 * 1024;

        // Before anything is collected: what the sessions left the process holding.
        Print($"memory at the end:  managed heap {GC.GetTotalMemory(false) / Megabyte,8:N0} MB, process private {process.PrivateMemorySize64 / Megabyte,8:N0} MB, peak working set {process.PeakWorkingSet64 / Megabyte,8:N0} MB");
        Print($"collections:        gen 0 {GC.CollectionCount(0)}, gen 1 {GC.CollectionCount(1)}, gen 2 {GC.CollectionCount(2)}");

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        process.Refresh();
        Print($"after collecting:   managed heap {GC.GetTotalMemory(false) / Megabyte,8:N0} MB, process private {process.PrivateMemorySize64 / Megabyte,8:N0} MB");
    }

    private void Print(string line)
    {
        output.WriteLine(line);
        _printed.AppendLine(line);
    }

    /// <summary>The shared scans, found by searching upward as the engine fixture does.</summary>
    private static string AssetPath(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "files", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not find '{name}' in any 'files' folder above '{AppContext.BaseDirectory}'.");
    }

    private sealed record Step(string Label, double Milliseconds, string? Failure)
    {
        /// <summary>One line for a step taken several times: its middle time, and any failure among them.</summary>
        public static Step MiddleOf(string label, List<Step> taken) => new(
            label,
            taken.Select(step => step.Milliseconds).Order().ElementAt(taken.Count / 2),
            taken.FirstOrDefault(step => step.Failure is not null)?.Failure);
    }
}
