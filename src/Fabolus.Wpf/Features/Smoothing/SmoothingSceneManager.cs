using System.Windows.Input;
using System.Windows.Media;
using Fabolus.Core.Features.Smoothing;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Features.Viewport;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Features.Smoothing;

public class SmoothingSceneManager : ISceneManager
{
    private readonly Material _rawSkin = DiffuseMaterials.SkyBlue;
    private readonly Material _smoothSkin = DiffuseMaterials.Emerald;

    private readonly IGeometryEngine _engine;
    private readonly IMessenger _messenger;

    private Element3D _grid;
    private CrossSectionMeshGeometryModel3D? _crossSectionModel;
    private CrossSectionMeshGeometryModel3D? _originalCrossSectionModel;
    private Plane _crossSectionPlane = new Plane { D = 0, Normal = Vector3.UnitZ };
    private Plane _originalCrossSectionPlane = new Plane { D = 0, Normal = -Vector3.UnitZ };
    private Guid _activeId = Guid.Empty;
    private Element3D _gizmo;
    private double _minZ = -double.MaxValue;
    private double _maxZ = double.MaxValue;
    private double _currentGizmoHeight = 0;

    private SmoothDisplayMode _displayMode;

    public event Action<Element3D>? VisualAddedOrUpdated;
    public event Action<Guid>? VisualRemovedById;
    public event Action? VisualsCleared;

    public SmoothingSceneManager(IGeometryEngine engine, IMessenger messenger)
    {
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

        _gizmo = CuttingPlane.Create(OnCuttingPlaneHeightChanged, () => _minZ, () => _maxZ);
    }

    public void SetDisplayMode(SmoothDisplayMode displayMode)
    {
        _displayMode = displayMode;
    }

    private void OnCuttingPlaneHeightChanged(double height)
    {
        _currentGizmoHeight = height;
        _crossSectionPlane = new Plane { D = (float)height, Normal = Vector3.UnitZ };
        _originalCrossSectionPlane = new Plane { D = (float)-height, Normal = -Vector3.UnitZ };

        if (_crossSectionModel != null)
        {
            _crossSectionModel.Plane1 = _crossSectionPlane;
        }
        if (_originalCrossSectionModel != null)
        {
            _originalCrossSectionModel.Plane1 = _originalCrossSectionPlane;
        }
    }

    /// <param name="mesh">The active (possibly smoothed) mesh.</param>
    /// <param name="unsmoothedMesh">The aligned unsmoothed counterpart to compare against in
    /// cross-section mode (BaseMesh with the mesh's other commands replayed on top, supplied
    /// by the view model). Only borrowed for this call - the caller may dispose it after.</param>
    public void UpdateMesh(IMesh mesh, IMesh? unsmoothedMesh = null, double[]? heatmapColors = null)
    {
        VisualRemovedById?.Invoke(_activeId);
        if (_crossSectionModel != null)
        {
            VisualRemovedById?.Invoke(_crossSectionModel.GUID);
        }
        if (_originalCrossSectionModel != null)
        {
            VisualRemovedById?.Invoke(_originalCrossSectionModel.GUID);
        }

        MeshGeometry3D geometry = mesh.ToHelixMesh(_engine, heatmapColors).Value;

        Material material;
        if (_displayMode == SmoothDisplayMode.Heatmap && heatmapColors != null)
        {
            material = new VertColorMaterial();
        }
        else
        {
            material = mesh.Metadata.GetSmoothing().HasValue ? _smoothSkin : _rawSkin;
        }

        var model = new MeshGeometryModel3D
        {
            Geometry = geometry,
            Material = material,
            CullMode = SharpDX.Direct3D11.CullMode.Back,
        };
        _activeId = model.GUID;

        bool isSmoothed = mesh.Metadata.GetSmoothing().HasValue;

        if (isSmoothed && unsmoothedMesh is not null && _displayMode == SmoothDisplayMode.CrossSection)
        {
            _gizmo.Visibility = System.Windows.Visibility.Visible;

            _crossSectionModel = GenerateCrossSection(geometry, _crossSectionPlane, _smoothSkin, Colors.Green);
            VisualAddedOrUpdated?.Invoke(_crossSectionModel);

            MeshGeometry3D originalGeometry = unsmoothedMesh.ToHelixMesh(_engine).Value;

            _minZ = Math.Min(geometry.Bound.Minimum.Z, originalGeometry.Bound.Minimum.Z) - 1.0;
            _maxZ = Math.Max(geometry.Bound.Maximum.Z, originalGeometry.Bound.Maximum.Z) + 1.0;

            double clampedHeight = Math.Clamp(_currentGizmoHeight, _minZ, _maxZ);
            if (Math.Abs(clampedHeight - _currentGizmoHeight) > 0.001)
            {
                // Update gizmo position if it's currently outside the new bounds
                _gizmo.Transform = new System.Windows.Media.Media3D.TranslateTransform3D(0, 0, clampedHeight);
            }

            _originalCrossSectionModel = GenerateCrossSection(originalGeometry, _originalCrossSectionPlane, _rawSkin, Colors.Red);
            VisualAddedOrUpdated?.Invoke(_originalCrossSectionModel);
        }
        else
        {
            _gizmo.Visibility = System.Windows.Visibility.Collapsed;
            _crossSectionModel = null;
            _originalCrossSectionModel = null;
            VisualAddedOrUpdated?.Invoke(model);
        }
    }

    public void OnActivated()
    {
        VisualsCleared?.Invoke();
        VisualAddedOrUpdated?.Invoke(_grid);
        VisualAddedOrUpdated?.Invoke(_gizmo);
        if (_crossSectionModel != null)
        {
            VisualAddedOrUpdated?.Invoke(_crossSectionModel);
        }
        if (_originalCrossSectionModel != null)
        {
            VisualAddedOrUpdated?.Invoke(_originalCrossSectionModel);
        }
    }

    public void OnDeactivated()
    {
    }

    public bool OnKeyDown(Key key) => false;

    public bool OnKeyUp(Key key) => false;

    public bool OnMouseDown(MouseDown3DEventArgs eventArgs) => false;

    public bool OnMouseMove(HelixToolkit.Wpf.SharpDX.HitTestResult? hit) => false;

    public bool OnMouseUp(MouseUp3DEventArgs eventArgs) => false;

    private CrossSectionMeshGeometryModel3D GenerateCrossSection(MeshGeometry3D geometry, Plane plane, Material material, System.Windows.Media.Color crossSectionColor)
    {
        return new CrossSectionMeshGeometryModel3D
        {
            Geometry = geometry,
            Material = material,
            CrossSectionColor = crossSectionColor,
            EnablePlane1 = true,
            Plane1 = plane,
            FillMode = SharpDX.Direct3D11.FillMode.Solid,
            CullMode = SharpDX.Direct3D11.CullMode.Back,
            CuttingOperation = CuttingOperation.Intersect,
            IsHitTestVisible = false,
        };
    }
}

