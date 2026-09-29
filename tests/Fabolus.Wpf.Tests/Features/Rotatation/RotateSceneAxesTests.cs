using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Rotatation;
using HelixToolkit.Wpf.SharpDX;
using Moq;
using Xunit;
using Colors = System.Windows.Media.Colors;

namespace Fabolus.Wpf.Tests.Features.Rotatation;

/// <summary>
/// The +X and +Y axes drawn on the bed in the rotation view.
/// </summary>
/// <remarks>
/// Rotation is the one view where which way the model faces is the whole question, and the bed
/// grid on its own is four-fold symmetric - it tells you nothing about which corner is which.
/// </remarks>
public class RotateSceneAxesTests
{
    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    [Fact]
    public void Activating_DrawsARedPositiveXAndAGreenPositiveYAxis() => UiThread.Run(async () =>
    {
        var lines = await AxisLinesAfterActivating(new StrongReferenceMessenger());

        var red = Assert.Single(lines, l => l.Color == Colors.Red);
        var green = Assert.Single(lines, l => l.Color == Colors.Green);

        // Default bed is 250x250 centred on the origin, so each axis runs from the centre out to
        // the edge at 125 - and out along the positive half only.
        Assert.Equal(new SharpDX.Vector2(125f, 0f), EndOf(red));
        Assert.Equal(new SharpDX.Vector2(0f, 125f), EndOf(green));
    });

    [Fact]
    public void BothAxesStartAtTheOrigin() => UiThread.Run(async () =>
    {
        var lines = await AxisLinesAfterActivating(new StrongReferenceMessenger());

        Assert.Equal(2, lines.Count);
        Assert.All(lines, line =>
        {
            var start = line.Geometry.Positions!.OrderBy(p => p.Length()).First();
            Assert.Equal(0f, start.X);
            Assert.Equal(0f, start.Y);
        });
    });

    /// <summary>
    /// The axes are bed furniture, so they follow the grid's own visibility rather than staying
    /// on over a bed the user asked to see bare.
    /// </summary>
    [Fact]
    public void HidingTheGrid_HidesTheAxesToo() => UiThread.Run(async () =>
    {
        var (messenger, lines) = await PanelWatchingLineVisuals();

        messenger.SaveSection(PrintBedPreferences.Default with { ShowGrid = false });

        var axes = Coloured(lines);
        Assert.Equal(2, axes.Count);
        Assert.All(axes, line =>
            Assert.Equal(System.Windows.Visibility.Collapsed, line.Visibility));
    });

    /// <summary>
    /// Changing the bed size replaces the grid, and an axis left at the old length would overhang
    /// a smaller bed or stop short of a larger one.
    /// </summary>
    [Fact]
    public void ResizingTheBed_ResizesTheAxes() => UiThread.Run(async () =>
    {
        var (messenger, lines) = await PanelWatchingLineVisuals();

        messenger.SaveSection(PrintBedPreferences.Default with { Width = 400f, Depth = 300f });

        var axes = Coloured(lines);
        var red = Assert.Single(axes, l => l.Color == Colors.Red);
        var green = Assert.Single(axes, l => l.Color == Colors.Green);

        Assert.Equal(new SharpDX.Vector2(200f, 0f), EndOf(red));
        Assert.Equal(new SharpDX.Vector2(0f, 150f), EndOf(green));
    });

    /// <summary>
    /// An activated rotation panel, plus the list its scene manager appends every line visual to
    /// from this point on. Nothing is collected from the activation itself, so what lands in the
    /// list is the response to whatever the test does next.
    /// </summary>
    private static async Task<(IMessenger, List<LineGeometryModel3D>)> PanelWatchingLineVisuals()
    {
        var messenger = new StrongReferenceMessenger();
        var vm = new RotateViewModel(messenger, Mock.Of<IAlertDialog>(), Engine);
        await vm.ActivateAsync(WorkspaceWithBox());

        var lines = new List<LineGeometryModel3D>();
        vm.SceneManager.VisualAddedOrUpdated += visual =>
        {
            if (visual is LineGeometryModel3D line) lines.Add(line);
        };

        return (messenger, lines);
    }

    /// <summary>The far end of a two-point axis line, as (x, y); the z lift is not the point.</summary>
    private static SharpDX.Vector2 EndOf(LineGeometryModel3D line)
    {
        var end = line.Geometry.Positions!.OrderByDescending(p => p.Length()).First();
        return new SharpDX.Vector2(end.X, end.Y);
    }

    /// <summary>
    /// The axis visuals out of a set of line visuals. The bed grid is a LineGeometryModel3D too,
    /// so it is told apart by colour - it never sets one, and defaults to white.
    /// </summary>
    private static List<LineGeometryModel3D> Coloured(IEnumerable<LineGeometryModel3D> lines) =>
        lines.Where(l => l.Color == Colors.Red || l.Color == Colors.Green).ToList();

    private static async Task<List<LineGeometryModel3D>> AxisLinesAfterActivating(IMessenger messenger)
    {
        var vm = new RotateViewModel(messenger, Mock.Of<IAlertDialog>(), Engine);
        await vm.ActivateAsync(WorkspaceWithBox());

        var lines = new List<LineGeometryModel3D>();
        vm.SceneManager.VisualAddedOrUpdated += visual =>
        {
            if (visual is LineGeometryModel3D line) lines.Add(line);
        };

        vm.SceneManager.OnActivated();

        return Coloured(lines);
    }

    private static Workspace WorkspaceWithBox()
    {
        var mesh = Engine.Generators.GenerateBox(new Vector3(-20, -30, 0), new Vector3(20, 30, 50)).Value
            .WithMeasurements(Engine);
        return Workspace.CreateEmpty().AddMesh(mesh, MeshRecord.ForImport("box")).Value;
    }
}
