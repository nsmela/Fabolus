using Fabolus.Core;
using Fabolus.Wpf.Common.Extensions;
using g3;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;
using Vector3D = System.Windows.Media.Media3D.Vector3D;

namespace Fabolus.Wpf.Common.Bolus;
public class BolusTransform {
    private List<Quaterniond> _rotations { get; set; } = [];

    public MeshGeometry3D ApplyTransforms(DMesh3 mesh) {
        var result = new DMesh3();
        result.Copy(mesh);

        _rotations.ForEach(transform => { MeshTransforms.Rotate(result, Vector3d.Zero, transform); });
        return result.ToGeometry();
    }

    #region Public Methods

    public void AddRotation(Vector3 axis, double angle) {
        var vector = new Vector3d(axis.X, axis.Y, axis.Z);
        _rotations.Add(new Quaterniond(vector, angle));
    }

    public void ClearTransforms() {
        _rotations.Clear();
    }

    #endregion

    #region Private

    #endregion
}
