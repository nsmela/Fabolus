using Fabolus.Core.Common;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;


namespace Fabolus.Wpf.Common.Mesh
{
    public static class MeshHelper
    {
        public static Vector3D VectorZero => new Vector3D(0,0,0);
        public static Transform3D TransformFromAxis(Vector3D axis, float angle) {
            var rotation = new AxisAngleRotation3D(axis, angle);
            var rotate = new RotateTransform3D(rotation);
            return new Transform3DGroup { Children = [rotate] };
        }
        public static Transform3D TransformFromAxis(Vector3 axis, float angle) =>
            TransformFromAxis(axis.ToVector3D(), angle);

        public static Transform3D TransformEmpty => TransformFromAxis(VectorZero, 0.0f);

    }
}
