using Fabolus.Wpf.Common.Scene;
using HelixToolkit.Wpf.SharpDX;
using MeshHelper = Fabolus.Wpf.Common.Mesh.MeshHelper;
using SharpDX;
using Fabolus.Wpf.Common.Mesh;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Bolus;
using Colors = System.Windows.Media.Colors;
using Vector3D = System.Windows.Media.Media3D.Vector3D;
using Transform3DGroup = System.Windows.Media.Media3D.Transform3DGroup;
using static Fabolus.Wpf.Stores.BolusStore;

namespace Fabolus.Wpf.Pages.Rotate;
public sealed class RotateSceneModel : SceneModel {
    private Vector3D _overhangAxis = new Vector3D(0, 0, -1);
    private Material _overhangSkin = new ColorStripeMaterial();
    private Transform3DGroup _transform = new Transform3DGroup();

    public RotateSceneModel() : base() {
        _overhangSkin = OverhangsHelper.CreateOverhangsMaterial();

        WeakReferenceMessenger.Default.Register<ApplyTempRotationMessage>(this, (r, m) => ApplyTempRotation(m.axis, m.angle));
        WeakReferenceMessenger.Default.Register<ApplyRotationMessage>(this, (r, m) => ApplyRotation());
    }

    private void ApplyTempRotation(Vector3D axis, float angle) {
        _transform = new Transform3DGroup();
        _transform = new Transform3DGroup { Children = [MeshHelper.TransformFromAxis(axis, -angle)] };
    }

    private void ApplyRotation() {
        _transform = new Transform3DGroup();
    }

    protected override void UpdateDisplay(BolusModel? bolus) {
        if (bolus is null || bolus.Geometry is null || bolus.Geometry.Positions.Count == 0) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessasge([]));
            return;
        }

        var refAxis = _transform.Transform(_overhangAxis).ToVector3();

        bolus.Geometry.TextureCoordinates = OverhangsHelper.GetTextureCoordinates(bolus.Geometry, refAxis);

        var models = new List<DisplayModel3D>();
        models.Add( new DisplayModel3D {
            Geometry = bolus.Geometry,
            Transform = bolus.Transform,
            Skin = _overhangSkin
        });

        //testing, to see how the ref angle is being managed properly
        if (true) {
            var mesh = new MeshBuilder();
            mesh.AddArrow(Vector3.Zero, refAxis, 3.0, 16);

            models.Add(new DisplayModel3D {
                Geometry = mesh.ToMeshGeometry3D(),
                Skin = PhongMaterials.Blue,
                Transform = MeshHelper.TransformEmpty
            });
        }

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessasge(models));
    }
}
