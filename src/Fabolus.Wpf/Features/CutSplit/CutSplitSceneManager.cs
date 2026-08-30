using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Features.Viewport;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Windows.Input;
using System.Windows.Media;
using Fabolus.Wpf.Common.Helpers;

namespace Fabolus.Wpf.Features.CutSplit;

public class CutSplitSceneManager : ISceneManager
{
    private readonly IGeometryEngine _engine;
    private readonly Material _topSkin = Skins.Surface.SkyBlue;
    private readonly Material _bottomSkin = Skins.Surface.Orange;

    private readonly PrintBedGrid _grid;
    private CrossSectionMeshGeometryModel3D? _topModel;
    private CrossSectionMeshGeometryModel3D? _bottomModel;
    private MeshGeometryModel3D? _planeVisual;
    private LineGeometryModel3D? _planeGridVisual;
    private UICompositeManipulator3D? _manipulator;
    private bool _isUpdating = false;
    
    private Plane _plane = new Plane(Vector3.UnitZ, 0f);
    private System.Numerics.Vector3 _origin = System.Numerics.Vector3.Zero;
    private System.Numerics.Vector3 _normal = System.Numerics.Vector3.UnitZ;

    public event Action<Element3D>? VisualAddedOrUpdated;
    public event Action<Guid>? VisualRemovedById;
    public event Action? VisualsCleared;

    // We can expose an event for widget manipulation if we had one
    public event Action<System.Numerics.Vector3, System.Numerics.Vector3>? PlaneChanged;

    public CutSplitSceneManager(IGeometryEngine engine, IMessenger messenger)
    {
        _engine = engine;

        _grid = new PrintBedGrid(messenger);
        _grid.Replaced += (replacedId, grid) =>
        {
            VisualRemovedById?.Invoke(replacedId);
            VisualAddedOrUpdated?.Invoke(grid);
        };
    }

    private IMesh? _activeMesh;

    public void UpdateMesh(IMesh mesh)
    {
        _activeMesh = mesh;
        RebuildVisuals();
        UpdatePlane(_origin, _normal);
    }

    public void ReleaseMesh()
    {
        _activeMesh = null;
        if (_topModel is not null) VisualRemovedById?.Invoke(_topModel.GUID);
        if (_bottomModel is not null) VisualRemovedById?.Invoke(_bottomModel.GUID);
        if (_planeVisual is not null) VisualRemovedById?.Invoke(_planeVisual.GUID);
        if (_planeGridVisual is not null) VisualRemovedById?.Invoke(_planeGridVisual.GUID);
        if (_manipulator is not null) VisualRemovedById?.Invoke(_manipulator.GUID);
        
        _planeVisual = null;
        _planeGridVisual = null;
        _manipulator = null;
    }

    public void UpdatePlane(System.Numerics.Vector3 origin, System.Numerics.Vector3 normal)
    {
        _origin = origin;
        _normal = normal;
        var dxNormal = new Vector3(normal.X, normal.Y, normal.Z);
        var dxOrigin = new Vector3(origin.X, origin.Y, origin.Z);
        float d = Vector3.Dot(dxNormal, dxOrigin);
        _plane = new Plane(dxNormal, d);

        if (_topModel is not null)
        {
            _topModel.Plane1 = _plane;
            _topModel.Transform = System.Windows.Media.Media3D.Transform3D.Identity;
        }
        if (_bottomModel is not null)
        {
            _bottomModel.Plane1 = new Plane(-dxNormal, -d);
            _bottomModel.Transform = System.Windows.Media.Media3D.Transform3D.Identity;
        }

        // Add a plane visual to represent the cut
        if (_planeVisual is null)
        {
            var meshBuilder = new MeshBuilder();
            meshBuilder.AddBox(new Vector3(0, 0, 0), 200, 200, 0.5f);
            _planeVisual = new MeshGeometryModel3D
            {
                Geometry = meshBuilder.ToMeshGeometry3D(),
                Material = new PhongMaterial { DiffuseColor = new Color4(0.0f, 0.5f, 1.0f, 0.3f), EmissiveColor = new Color4(0.0f, 0.2f, 0.4f, 1.0f) },
                IsTransparent = true,
                CullMode = SharpDX.Direct3D11.CullMode.None
            };
            VisualAddedOrUpdated?.Invoke(_planeVisual);
        }

        if (_planeGridVisual is null)
        {
            var lineBuilder = new LineBuilder();
            lineBuilder.AddLine(new Vector3(-100, -100, 0), new Vector3(100, -100, 0));
            lineBuilder.AddLine(new Vector3(100, -100, 0), new Vector3(100, 100, 0));
            lineBuilder.AddLine(new Vector3(100, 100, 0), new Vector3(-100, 100, 0));
            lineBuilder.AddLine(new Vector3(-100, 100, 0), new Vector3(-100, -100, 0));

            for (int i = -80; i <= 80; i += 20)
            {
                lineBuilder.AddLine(new Vector3(i, -100, 0), new Vector3(i, 100, 0));
                lineBuilder.AddLine(new Vector3(-100, i, 0), new Vector3(100, i, 0));
            }

            _planeGridVisual = new LineGeometryModel3D
            {
                Geometry = lineBuilder.ToLineGeometry3D(),
                Color = Colors.Cyan,
                Thickness = 1.5,
                IsHitTestVisible = false
            };
            VisualAddedOrUpdated?.Invoke(_planeGridVisual);
        }

        var axis = System.Numerics.Vector3.Cross(System.Numerics.Vector3.UnitZ, normal);
        float dot = System.Numerics.Vector3.Dot(System.Numerics.Vector3.UnitZ, normal);
        System.Numerics.Quaternion q;
        if (dot < -0.9999f) q = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX, (float)Math.PI);
        else if (dot > 0.9999f) q = System.Numerics.Quaternion.Identity;
        else q = System.Numerics.Quaternion.Normalize(new System.Numerics.Quaternion(axis, 1 + dot));

        var transform = new System.Windows.Media.Media3D.Transform3DGroup();
        transform.Children.Add(new System.Windows.Media.Media3D.RotateTransform3D(new System.Windows.Media.Media3D.QuaternionRotation3D(new System.Windows.Media.Media3D.Quaternion(q.X, q.Y, q.Z, q.W))));
        transform.Children.Add(new System.Windows.Media.Media3D.TranslateTransform3D(origin.X, origin.Y, origin.Z));
        
        _planeVisual.Transform = transform;
        _planeGridVisual.Transform = transform;

        if (_manipulator is null)
        {
            _manipulator = new UICompositeManipulator3D
            {
                CanTranslateX = true,
                CanTranslateY = true,
                CanTranslateZ = true,
                CanRotateX = true,
                CanRotateY = true,
                CanRotateZ = false,
                Diameter = 40.0
            };

            var binding = new System.Windows.Data.Binding(nameof(UICompositeManipulator3D.TargetTransform))
            {
                Source = _manipulator,
                Mode = System.Windows.Data.BindingMode.OneWay
            };
            System.Windows.Data.BindingOperations.SetBinding(_manipulator, Element3D.TransformProperty, binding);

            var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                UICompositeManipulator3D.TargetTransformProperty, typeof(UICompositeManipulator3D));
                
            descriptor.AddValueChanged(_manipulator, (s, e) =>
            {
                if (_isUpdating) return;

                System.Windows.Media.Media3D.Matrix3D m;
                if (_manipulator.TargetTransform is System.Windows.Media.Media3D.MatrixTransform3D mt)
                {
                    m = mt.Matrix;
                }
                else if (_manipulator.TargetTransform is System.Windows.Media.Media3D.Transform3DGroup tg)
                {
                    m = tg.Value;
                }
                else
                {
                    return;
                }

                var newOrigin = new System.Numerics.Vector3((float)m.OffsetX, (float)m.OffsetY, (float)m.OffsetZ);
                var newNormal = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3((float)m.M31, (float)m.M32, (float)m.M33));

                _isUpdating = true;
                PlaneChanged?.Invoke(newOrigin, newNormal);
                _isUpdating = false;
            });

            VisualAddedOrUpdated?.Invoke(_manipulator);
        }

        if (!_isUpdating)
        {
            _manipulator.TargetTransform = transform;
        }
    }

    private void RebuildVisuals()
    {
        if (_topModel is not null) VisualRemovedById?.Invoke(_topModel.GUID);
        if (_bottomModel is not null) VisualRemovedById?.Invoke(_bottomModel.GUID);

        if (_activeMesh is null) return;

        var helixMeshResult = _activeMesh.ToHelixMesh(_engine);
        if (helixMeshResult.IsFailure) return;

        var geometry = helixMeshResult.Value;

        _topModel = new CrossSectionMeshGeometryModel3D
        {
            Geometry = geometry,
            Material = _topSkin,
            CrossSectionColor = Colors.LightBlue,
            EnablePlane1 = true,
            Plane1 = _plane,
            FillMode = SharpDX.Direct3D11.FillMode.Solid,
            CullMode = SharpDX.Direct3D11.CullMode.Back,
            CuttingOperation = CuttingOperation.Intersect,
            IsHitTestVisible = false,
        };

        _bottomModel = new CrossSectionMeshGeometryModel3D
        {
            Geometry = geometry,
            Material = _bottomSkin,
            CrossSectionColor = Colors.OrangeRed,
            EnablePlane1 = true,
            Plane1 = new Plane(-_plane.Normal, -_plane.D),
            FillMode = SharpDX.Direct3D11.FillMode.Solid,
            CullMode = SharpDX.Direct3D11.CullMode.Back,
            CuttingOperation = CuttingOperation.Intersect,
            IsHitTestVisible = false,
        };

        SceneVisual.SetIsModelGeometry(_topModel, true);
        SceneVisual.SetIsModelGeometry(_bottomModel, true);

        VisualAddedOrUpdated?.Invoke(_topModel);
        VisualAddedOrUpdated?.Invoke(_bottomModel);
    }

    public void OnActivated()
    {
        VisualsCleared?.Invoke();
        VisualAddedOrUpdated?.Invoke(_grid.Current);
        if (_topModel is not null) VisualAddedOrUpdated?.Invoke(_topModel);
        if (_bottomModel is not null) VisualAddedOrUpdated?.Invoke(_bottomModel);
        if (_planeVisual is not null) VisualAddedOrUpdated?.Invoke(_planeVisual);
        if (_planeGridVisual is not null) VisualAddedOrUpdated?.Invoke(_planeGridVisual);
        if (_manipulator is not null) VisualAddedOrUpdated?.Invoke(_manipulator);
    }

    public void OnDeactivated() { }

    public bool OnKeyDown(Key key) => false;
    public bool OnKeyUp(Key key) => false;
    public bool OnMouseDown(MouseDown3DEventArgs eventArgs) => false;
    public bool OnMouseMove(IList<HelixToolkit.Wpf.SharpDX.HitTestResult> hits) => false;
    public bool OnMouseUp(MouseUp3DEventArgs eventArgs) => false;
}
