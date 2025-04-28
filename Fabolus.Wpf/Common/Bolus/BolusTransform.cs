using Fabolus.Core;
using Fabolus.Core.Meshes;
using Fabolus.Wpf.Common.Extensions;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;
using Vector3D = System.Windows.Media.Media3D.Vector3D;
using Quaternion = SharpDX.Quaternion;

namespace Fabolus.Wpf.Common.Bolus;
public class BolusTransform {
    private List<Quaternion> _rotations { get; set; } = [];

    #region Public Methods

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

    #endregion

}
