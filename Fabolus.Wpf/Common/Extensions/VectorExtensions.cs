using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using g3;
using SharpDX;

namespace Fabolus.Wpf.Common.Extensions;

public static class VectorExtensions {
    public static Vector3 ToVector3(this Vector3d vector) =>
        new Vector3 { X = (float)vector.x, Y = (float)vector.y, Z = (float)vector.z };

    public static Vector3 ToVector3(this Vector3f vector) =>
        new Vector3 { X = vector.x, Y = vector.y, Z = vector.z };

    public static IEnumerable<Vector3> ToVector3(this IList<Vector3d> vectors) {
        foreach(var v in vectors) { yield return v.ToVector3(); }
    }

    public static Vector3d ToVector3d(this Vector3 vector) =>
        new Vector3d { x = vector.X, y = vector.Y, z = vector.Z };

    public static Vector3d ToVector3d(this Vector3D vector) =>
        new Vector3d { x = vector.X, y = vector.Y, z = vector.Z };
}
