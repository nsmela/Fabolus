using Fabolus.Core.Common;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Features.Viewport;
using HelixToolkit.Wpf.SharpDX;
using System.Numerics;
using System.Windows.Input;

namespace Fabolus.Wpf.Features.Moulding;

public class MouldSceneManager : ISceneManager
{
    private readonly IGeometryEngine _engine;

    private Workspace Workspace { get; set; }
    private IReadOnlyList<IAirChannel> Channels { get; set; } = [];
    private IAirChannel PreviewChannel { get; set; }
    private MouldDefinition MouldDefinition { get; set; }

    public event Action<Element3D> VisualAddedOrUpdated;
    public event Action<Guid> VisualRemovedById;
    public event Action VisualsCleared;

    public MouldSceneManager(IGeometryEngine engine)
    {
        _engine = engine;
    }

    public Result UpdateWorkspace(Workspace workspace)
    {
        Workspace = workspace;

        return Result.Success();
    }

    public Result UpdateChannels(IEnumerable<IAirChannel> channels)
    {

        return Result.Success();
    }

    public void UpdatePreviewChannel(IAirChannel channel)
    {
        Vector3 point = Vector3.Zero;
        PreviewChannel = channel;
    }

    public Result UpdateMould(MouldDefinition mouldDefinition)
    {
        MouldDefinition = mouldDefinition;

        return Result.Success();
    }

    public void OnActivated() { }

    public void OnDeactivated() { }

    public bool OnKeyDown(Key key) => false;

    public bool OnKeyUp(Key key) => false;

    public bool OnMouseDown(MouseDown3DEventArgs eventArgs) => false;

    public bool OnMouseMove(MouseMove3DEventArgs eventArgs) => false;

    public bool OnMouseUp(MouseUp3DEventArgs eventArgs) => false;
}
