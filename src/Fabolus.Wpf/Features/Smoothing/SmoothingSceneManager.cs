using Fabolus.Core.Features.Smoothing;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Features.Viewport;
using HelixToolkit.Wpf.SharpDX;
using System.Windows.Input;

namespace Fabolus.Wpf.Features.Smoothing;

public class SmoothingSceneManager : ISceneManager {
    private readonly Material _rawSkin = DiffuseMaterials.SkyBlue;
    private readonly Material _smoothSkin = DiffuseMaterials.Green;

    private readonly IGeometryEngine _engine;

    private readonly Element3D _grid;
    private Guid _activeId = Guid.Empty;

    public event Action<Element3D>? VisualAddedOrUpdated;
    public event Action<Guid>? VisualRemovedById;
    public event Action? VisualsCleared;

    public SmoothingSceneManager(IGeometryEngine engine) {
        _engine = engine;
        _grid = SceneHelpers.GenerateGrid();
    }

    public void UpdateWorkspace(Workspace workspace) {
        VisualRemovedById?.Invoke(_activeId);

        var activeMeshResult = workspace.GetActiveMesh();
        if (activeMeshResult.IsFailure) {
            return;
        }

        IMesh mesh = activeMeshResult.Value;

        MeshGeometry3D geometry = mesh.ToHelixMesh(_engine).Value;
        var model = new MeshGeometryModel3D {
            Geometry = geometry,
            Material = mesh.Metadata.GetSmoothing().HasValue
                ? _smoothSkin 
                : _rawSkin,
        };
        _activeId = model.GUID;

        VisualAddedOrUpdated?.Invoke(model);
    }

    public void OnActivated() {
        VisualsCleared?.Invoke();
        VisualAddedOrUpdated?.Invoke(_grid);
    }

    public void OnDeactivated() {
    }

    public bool OnKeyDown(Key key) => false;

    public bool OnKeyUp(Key key) => false;

    public bool OnMouseDown(MouseDown3DEventArgs eventArgs) => false;

    public bool OnMouseMove(MouseMove3DEventArgs eventArgs) => false;

    public bool OnMouseUp(MouseUp3DEventArgs eventArgs) => false;
}
