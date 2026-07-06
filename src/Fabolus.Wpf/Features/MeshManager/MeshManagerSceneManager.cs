using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Features.Viewport;
using HelixToolkit.Wpf.SharpDX;
using System.Windows.Input;

namespace Fabolus.Wpf.Features.MeshManager;

internal class MeshManagerSceneManager : ISceneManager {
    private readonly IGeometryEngine _engine;

    public event Action<Element3D> VisualAddedOrUpdated;
    public event Action<Guid> VisualRemovedById;
    public event Action VisualsCleared;

    private readonly Element3D _grid;
    private Guid _activeId = Guid.Empty;

    public MeshManagerSceneManager(IGeometryEngine engine) {
        _engine = engine;
        _grid = SceneHelpers.GenerateGrid();
    }

    public void UpdateWorkspace(Workspace workspace) {
        VisualRemovedById?.Invoke(_activeId);

        var activeMeshResult = workspace.GetActiveMesh();
        if (activeMeshResult.IsFailure) return;

        // Owned copy - converted to render geometry immediately, then released.
        using IMesh mesh = activeMeshResult.Value;

        MeshGeometry3D geometry = mesh.ToHelixMesh(_engine).Value;
        var model = new MeshGeometryModel3D {
            Geometry = geometry,
            Material = DiffuseMaterials.Gray,
        };
        _activeId = model.GUID;

        VisualAddedOrUpdated?.Invoke(model);
    }

    public void OnActivated() {
        VisualsCleared?.Invoke();
        VisualAddedOrUpdated?.Invoke(_grid);
    }

    public void OnDeactivated() { }

    public bool OnKeyDown(Key key) => false;
    public bool OnKeyUp(Key key) => false;

    public bool OnMouseDown(MouseDown3DEventArgs eventArgs) => false;

    public bool OnMouseMove(HitTestResult? hit) => false;

    public bool OnMouseUp(MouseUp3DEventArgs eventArgs) => false;

}
