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
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Rotate;

public sealed class RotateSceneModel : SceneModel {
    private Vector3D _overhangAxis = new Vector3D(0, 0, -1);
    private Material _overhangSkin = new ColorStripeMaterial();

    public RotateSceneModel() : base() {
        _overhangSkin = OverhangsHelper.CreateOverhangsMaterial();
    }

    protected override void UpdateDisplay(BolusModel? bolus) {
        if (bolus is null || bolus.Geometry is null || bolus.Geometry.Positions.Count == 0) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessasge([]));
            return;
        }

        //overhangs rely on the normals of the mesh
        //temp rotations ened to be calculated with the oposite angle

        var transform = bolus.Transforms.Rotation;
        var refAxis = bolus.Transforms.ApplyAxisRotation(_overhangAxis).ToVector3();

        bolus.Geometry.TextureCoordinates = OverhangsHelper.GetTextureCoordinates(bolus.Geometry, refAxis);

        var models = new List<DisplayModel3D>();
        models.Add( new DisplayModel3D {
            Geometry = bolus.Geometry,
            Transform = transform,
            Skin = _overhangSkin
        });

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessasge(models));
    }
}
