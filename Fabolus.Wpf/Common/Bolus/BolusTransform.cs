using Fabolus.Core.Meshes;
using SharpDX;
using Quaternion = SharpDX.Quaternion;

namespace Fabolus.Wpf.Common.Bolus;
public class BolusTransform {
    private List<Quaternion> _rotations { get; set; } = [];

    public MeshModel ApplyTransforms(MeshModel mesh) {
        var result = MeshModel.Copy(mesh);

        _rotations.ForEach(r => result.ApplyTransform(r.X, r.Y, r.Z, r.W));
        return result;
    }

    public void AddRotation(Vector3 axis, float angle) {
        var vector = new Vector3(axis.X, axis.Y, axis.Z);
        _rotations.Add(new Quaternion(vector, angle));
    }

    public void ClearTransforms() {
        _rotations.Clear();
    }

}
