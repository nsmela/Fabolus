using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Features.Viewport;
using HelixToolkit.Wpf.SharpDX;
using System.Windows.Input;
using System;
using Fabolus.Wpf.Common.Helpers;

namespace Fabolus.Wpf.Features.Export;

internal class ExportSceneManager : ISceneManager {
    private readonly IGeometryEngine _engine;
    private readonly IMessenger _messenger;

    public event Action<Element3D> VisualAddedOrUpdated;
    public event Action<Guid> VisualRemovedById;
    public event Action VisualsCleared;

    private Element3D _grid;
    private Guid _activeId = Guid.Empty;

    public ExportSceneManager(IGeometryEngine engine, IMessenger messenger) {
        _engine = engine;
        _messenger = messenger;

        var width = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedWidthLabel)).Response;
        var depth = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedDepthLabel)).Response;
        var show = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.ShowBedGridLabel)).Response;
        _grid = SceneHelpers.GenerateGrid(width, depth, 10, show);

        _messenger.Register<AppPreferenceUpdateMessage>(this, (r, m) => {
            if (m.Key == UISettings.PrintBedWidthLabel || m.Key == UISettings.PrintBedDepthLabel || m.Key == UISettings.ShowBedGridLabel) {
                var w = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedWidthLabel)).Response;
                var d = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedDepthLabel)).Response;
                var s = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.ShowBedGridLabel)).Response;
                
                if (_grid != null) {
                    VisualRemovedById?.Invoke(_grid.GUID);
                }
                _grid = SceneHelpers.GenerateGrid(w, d, 10, s);
                VisualAddedOrUpdated?.Invoke(_grid);
            }
        });
    }

    public void UpdateWorkspace(Workspace workspace) {
        VisualRemovedById?.Invoke(_activeId);

        var activeMeshResult = workspace.GetActiveMesh();
        if (activeMeshResult.IsFailure) return;

        // Owned copy - converted to render geometry immediately, then released.
        IMesh mesh = activeMeshResult.Value;

        MeshGeometry3D geometry = mesh.ToHelixMesh(_engine).Value;
        var model = new MeshGeometryModel3D {
            Geometry = geometry,
            Material = Skins.Surface.Gray,
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
