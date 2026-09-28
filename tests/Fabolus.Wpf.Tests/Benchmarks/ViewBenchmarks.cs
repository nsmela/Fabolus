using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Features.Decal;
using Fabolus.Wpf.Features.Main;
using Fabolus.Wpf.Features.MeshManager;
using Fabolus.Wpf.Features.Moulding;
using Fabolus.Wpf.Features.Rotatation;
using Fabolus.Wpf.Features.Smoothing;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Wpf.Tests.Benchmarks;

/// <summary>
/// A benchmark, not a test: it asserts nothing and only prints timings. Skipped unless
/// FABOLUS_BENCH is set, so a normal test run neither pays for it nor fails on a slow machine.
/// </summary>
public sealed class BenchmarkFactAttribute : FactAttribute
{
    public BenchmarkFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FABOLUS_BENCH")))
        {
            Skip = "Benchmark. Run with FABOLUS_BENCH=1.";
        }
    }
}

/// <summary>
/// What the user waits for when a view opens, measured on real bolus scans rather than the
/// 12-triangle box the behaviour tests use. Each view is timed as the app drives it - construct
/// the view model, then ActivateAsync - and then the pieces underneath are timed separately so a
/// slow activation can be attributed rather than guessed at.
/// </summary>
public class ViewBenchmarks
{
    private readonly ITestOutputHelper _out;

    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    public ViewBenchmarks(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// A small, a middling and a large scan, so a cost that grows with the mesh is visible as a
    /// trend rather than as one number that may or may not be typical.
    /// </summary>
    private static readonly string[] Meshes =
    [
        "eye_bolus.stl",
        "chin_bolus.stl",
        "scalp_bolus.stl",
        "test_smoothed_bolus.stl",
    ];

    [BenchmarkFact]
    public void RotateViewOpening() => OnUiThread(async () =>
    {
        foreach (var name in Meshes)
        {
            var (workspace, mesh) = Load(name);
            Report(name, mesh);

            var vm = new RotateViewModel(new StrongReferenceMessenger(), Mock.Of<IAlertDialog>(), Engine);

            _out.WriteLine($"    activate (cold)     {await TimeAsync(() => vm.ActivateAsync(workspace))}");
            _out.WriteLine($"    activate (again)    {await TimeAsync(() => vm.ActivateAsync(workspace))}");
            _out.WriteLine("");
        }
    });

    [BenchmarkFact]
    public void SmoothingViewOpening() => OnUiThread(async () =>
    {
        foreach (var name in Meshes)
        {
            var (workspace, mesh) = Load(name);
            Report(name, mesh);

            var vm = new SmoothingViewModel(new StrongReferenceMessenger(), Mock.Of<IAlertDialog>(), Engine);

            _out.WriteLine($"    activate (cold)     {await TimeAsync(() => vm.ActivateAsync(workspace))}");
            _out.WriteLine($"    activate (again)    {await TimeAsync(() => vm.ActivateAsync(workspace))}");
            _out.WriteLine("");
        }
    });

    [BenchmarkFact]
    public void MouldViewOpening() => OnUiThread(async () =>
    {
        foreach (var name in Meshes)
        {
            var (workspace, mesh) = Load(name);
            Report(name, mesh);

            var vm = new MouldViewModel(new StrongReferenceMessenger(), Mock.Of<IAlertDialog>(), Engine);

            _out.WriteLine($"    activate (cold)     {await TimeAsync(() => vm.ActivateAsync(workspace))}");
            _out.WriteLine($"    activate (again)    {await TimeAsync(() => vm.ActivateAsync(workspace))}");
            _out.WriteLine("");
        }
    });

    [BenchmarkFact]
    public void DecalViewOpeningApplyAndClear() => OnUiThread(async () =>
    {
        foreach (var name in Meshes)
        {
            var (workspace, mesh) = Load(name);
            Report(name, mesh);

            var vm = new DecalViewModel(new StrongReferenceMessenger(), Mock.Of<IAlertDialog>(), Engine, new BenchOutlineSource());

            _out.WriteLine($"    activate (cold)     {await TimeAsync(() => vm.ActivateAsync(workspace))}");
            _out.WriteLine($"    activate (again)    {await TimeAsync(() => vm.ActivateAsync(workspace))}");
            _out.WriteLine($"    apply               {await TimeAsync(() => vm.ApplyCommand.ExecuteAsync(null))}");
            _out.WriteLine($"    clear               {await TimeAsync(() => vm.ClearTextCommand.ExecuteAsync(null))}");
            _out.WriteLine("");
        }
    });

    /// <summary>
    /// The deliberate, button-pressed operations. Unlike a slider step these are expected to
    /// take a moment; what matters is whether the user is told that they are happening.
    /// </summary>
    [BenchmarkFact]
    public void CommittedOperations() => OnUiThread(async () =>
    {
        foreach (var name in Meshes)
        {
            var (workspace, mesh) = Load(name);
            Report(name, mesh);

            var smooth = new SmoothingViewModel(new StrongReferenceMessenger(), Mock.Of<IAlertDialog>(), Engine);
            await smooth.ActivateAsync(workspace);
            _out.WriteLine($"    smoothing: apply     {await TimeAsync(() => smooth.ApplySmoothingCommand.ExecuteAsync(null))}");

            var mould = new MouldViewModel(new StrongReferenceMessenger(), Mock.Of<IAlertDialog>(), Engine);
            await mould.ActivateAsync(workspace);
            _out.WriteLine($"    mould: generate      {await TimeAsync(() => mould.GenerateMouldCommand.ExecuteAsync(null))}");
            _out.WriteLine("");
        }
    });

    /// <summary>
    /// Commands that do geometry work straight on the UI thread. What matters here is not only
    /// how long they take but whether the overlay is up over the part that takes it.
    /// </summary>
    [BenchmarkFact]
    public void BlockingCommands() => OnUiThread(async () =>
    {
        foreach (var name in Meshes)
        {
            var (workspace, mesh) = Load(name);
            Report(name, mesh);

            var rotate = new RotateViewModel(new StrongReferenceMessenger(), Mock.Of<IAlertDialog>(), Engine);
            await rotate.ActivateAsync(workspace);
            rotate.XAxisAngle = 15f;
            _out.WriteLine($"    rotate: save rotation    {await TimeHeld(() => rotate.SaveAxisRotationCommand.ExecuteAsync(null))}");
            _out.WriteLine($"    rotate: clear rotations  {await TimeHeld(() => rotate.ClearRotationsCommand.ExecuteAsync(null))}");

            var meshes = new MeshManagerViewModel(
                new StrongReferenceMessenger(), Mock.Of<IDialogueSystem>(), Mock.Of<IAlertDialog>(), Engine);
            await meshes.ActivateAsync(workspace);
            var activeId = workspace.ActiveMeshId;
            _out.WriteLine($"    meshes: activate         {await TimeAsync(() => meshes.ActivateAsync(workspace))}");
            _out.WriteLine($"    meshes: repair           {await TimeHeld(() => meshes.RepairMeshCommand.ExecuteAsync(activeId))}");
            _out.WriteLine("");
        }
    });

    /// <summary>
    /// The one-off cost the first mesh render in the process pays, whichever view happens to be
    /// showing. Measured on the smallest mesh so what is left is startup, not size.
    /// </summary>
    [BenchmarkFact]
    public void FirstRenderWarmUp()
    {
        var (_, small) = Load("eye_bolus.stl");
        _out.WriteLine($"ToHelixMesh, first call in process  {Time(() => small.ToHelixMesh(Engine))}");
        _out.WriteLine($"ToHelixMesh, second call            {Time(() => small.ToHelixMesh(Engine))}");
        _out.WriteLine($"ToHelixMesh, third call             {Time(() => small.ToHelixMesh(Engine))}");
    }

    /// <summary>
    /// One step of a slider drag. Opening a view is paid once; these handlers run on every tick
    /// the user drags through, so a cost here is multiplied by however many steps WPF raises.
    /// </summary>
    [BenchmarkFact]
    public void SliderDragSteps() => OnUiThread(async () =>
    {
        foreach (var name in Meshes)
        {
            var (workspace, mesh) = Load(name);
            Report(name, mesh);

            // A drag, not a step: twenty changes as fast as WPF would raise them. What matters is
            // how long the UI thread is held, and how long until the shell catches up - the
            // rebuild is deferred and coalesced, so those are now two different numbers.
            var messenger = new StrongReferenceMessenger();
            var mould = new MouldViewModel(messenger, Mock.Of<IAlertDialog>(), Engine);
            await mould.ActivateAsync(workspace);

            var loading = new LoadingLog();
            messenger.Register<LoadingLog, IsLoadingMessage>(loading, (r, m) =>
            {
                if (m.IsLoading) r.Raised++; else r.Lowered++;
            });

            var drag = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++)
            {
                mould.WallThickness += 0.1;
            }
            var held = drag.Elapsed;

            while (loading.Lowered == 0 && drag.Elapsed < TimeSpan.FromSeconds(60))
            {
                await Task.Delay(5);
            }

            _out.WriteLine($"    mould: 20-step drag, UI thread held   {held.TotalMilliseconds,9:N1} ms");
            _out.WriteLine($"    mould: 20-step drag, shell caught up  {drag.Elapsed.TotalMilliseconds,9:N1} ms");
            _out.WriteLine($"    mould: rebuilds run for those 20      {loading.Raised,9}");

            var rotate = new RotateViewModel(new StrongReferenceMessenger(), Mock.Of<IAlertDialog>(), Engine);
            await rotate.ActivateAsync(workspace);
            _out.WriteLine($"    rotate: overhang angle   {Time(() => rotate.WarningAngle += 1f)}");
            _out.WriteLine($"    rotate: temp rotation    {Time(() => rotate.XAxisAngle += 1f)}");

            var smooth = new SmoothingViewModel(new StrongReferenceMessenger(), Mock.Of<IAlertDialog>(), Engine);
            await smooth.ActivateAsync(workspace);
            _out.WriteLine($"    smoothing: comparison    {Time(() => smooth.ComparisonFactor += 0.05)}");
            _out.WriteLine($"    smoothing: heatmap sens  {Time(() => smooth.HeatmapSensitivity += 0.05)}");
            _out.WriteLine("");
        }
    });

    /// <summary>
    /// The shared pieces every view leans on, timed on their own so the activation numbers above
    /// can be attributed to one of them rather than to the view.
    /// </summary>
    [BenchmarkFact]
    public void SharedPrimitives()
    {
        foreach (var name in Meshes)
        {
            var (_, mesh) = Load(name);
            Report(name, mesh);

            _out.WriteLine($"    import from disk    {Time(() => Engine.IO.Import(PathTo(name)))}");
            _out.WriteLine($"    GetStatistics       {Time(() => Engine.Evaluators.GetStatistics(mesh))}");
            _out.WriteLine($"    ValidateTopology    {Time(() => Engine.Evaluators.ValidateTopology(mesh))}");
            _out.WriteLine($"    BuildIndex          {Time(() => Engine.Spatial.BuildIndex(mesh))}");
            _out.WriteLine($"    ToHelixMesh         {Time(() => mesh.ToHelixMesh(Engine))}");
            _out.WriteLine($"    base presets   (3 rays)  {Time(() => BasePresetPointsCalculator.Calculate(Engine, mesh))}");
            _out.WriteLine($"    mould presets (76 rays)  {Time(() => MouldPresetPointsCalculator.Calculate(Engine, mesh))}");
            _out.WriteLine("");
        }
    }

    // ---- plumbing -------------------------------------------------------------------------

    /// <summary>
    /// Runs the body on an STA thread with a real dispatcher, the way the app runs it - so the
    /// marshalling back to the UI thread stays inside the numbers, where it belongs.
    /// </summary>
    private static void OnUiThread(Func<Task> body) => UiThread.Run(body);

    private void Report(string name, IMesh mesh)
    {
        var stats = Engine.Evaluators.GetStatistics(mesh);
        var triangles = stats.IsSuccess ? stats.Value.TriangleCount.ToString("N0") : "?";
        _out.WriteLine($"{name}  ({triangles} triangles)");
    }

    private static string Time(Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        return $"{sw.Elapsed.TotalMilliseconds,9:N1} ms";
    }

    /// <summary>
    /// Splits a command's cost into the part that holds the UI thread - everything before it
    /// first awaits - and the whole thing. The overlay can only paint during the difference, so
    /// "held" is what decides whether the window freezes.
    /// </summary>
    private static async Task<string> TimeHeld(Func<Task> start)
    {
        var sw = Stopwatch.StartNew();
        var pending = start();
        var held = sw.Elapsed;
        await pending;

        return $"{held.TotalMilliseconds,7:N1} ms held / {sw.Elapsed.TotalMilliseconds,7:N1} ms total";
    }

    private static async Task<string> TimeAsync(Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        await action();
        sw.Stop();
        return $"{sw.Elapsed.TotalMilliseconds,9:N1} ms";
    }

    private static (Workspace Workspace, IMesh Mesh) Load(string name)
    {
        var mesh = Engine.IO.Import(PathTo(name)).Value;
        var record = MeshRecord.ForImport(Path.GetFileNameWithoutExtension(name));
        return (Workspace.CreateEmpty().AddMesh(mesh, record).Value, mesh);
    }

    /// <summary>
    /// Walks up from the test output folder to the shared tests/files directory, the same way
    /// the core test fixture does - the output layout gains a directory when a platform is set,
    /// so a fixed number of hops gets it wrong.
    /// </summary>
    private static string PathTo(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "files", name);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find '{name}' in any 'files' folder above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    /// Counts the overlay going up and down, which is how a caller outside the view model can see
    /// a deferred rebuild start and finish.
    /// </summary>
    private sealed class LoadingLog
    {
        public int Raised;
        public int Lowered;
    }

    /// <summary>
    /// A fixed rectangle per glyph. The point is to measure what the views do with a prism, not
    /// how fast WPF can turn a font into outlines.
    /// </summary>
    private sealed class BenchOutlineSource : IGlyphOutlineSource
    {
        public BasicResults.Result<IReadOnlyList<Polygon2D>> GetOutlines(string text, DecalFont font, float capHeight, float tracking) =>
            BasicResults.Result.Success<IReadOnlyList<Polygon2D>>(
                [Polygon2D.FromOuter([new(-5, -3), new(5, -3), new(5, 3), new(-5, 3)])]);

        public TextMetrics MeasureText(string text, DecalFont font, float capHeight, float tracking) =>
            new(10f, capHeight, [10f]);
    }
}
