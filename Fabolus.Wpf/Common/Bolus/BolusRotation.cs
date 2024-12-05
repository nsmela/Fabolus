using Fabolus.Wpf.Common.Mesh;
using System.Windows.Media.Media3D;

namespace Fabolus.Wpf.Common.Bolus;

public class BolusRotation {
    public Transform3DGroup Transforms { get; set; } = new Transform3DGroup();
    public Transform3D Rotation {
        get {
            var result = new Transform3DGroup();
            foreach(var transform in Transforms.Children) { result.Children.Add(transform); }
            result.Children.Add(TempTransform);
            return result;
        }
    }
    public float Angle { get; set; } = 0.0f;
    public Vector3D Axis { get; set; } = new Vector3D(0, 0, 1);
    public void ApplyRotation(Vector3D axis, float angle) {
        Transforms.Children.Add(MeshHelper.TransformFromAxis(axis, angle));
        Axis = new Vector3D(0, 0, 1);
        Angle = 0.0f;
    }
    public void AddTempRotation(Vector3D axis, float angle) {
        Angle = angle;
        Axis = axis;
    }
    public Transform3D TempTransform => MeshHelper.TransformFromAxis(Axis, Angle);
    public Vector3D ApplyAxisRotation(Vector3D axis) {
        var result = new Transform3DGroup();
        foreach (var transform in Transforms.Children) { result.Children.Add(transform); }
        result.Children.Add(MeshHelper.TransformFromAxis(Axis, Angle));
        return result.Transform(axis);
    }

}
