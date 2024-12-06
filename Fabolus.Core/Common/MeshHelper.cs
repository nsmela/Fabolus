using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;

namespace Fabolus.Core.Common;
internal static class MeshHelper {

    //from HelixToolkit.Shared.Geometry.MeshGeometryHelpers
    public static Vector3Collection CalculateNormals(MeshGeometry3D geometry) {
        IList<Vector3> positions = geometry.Positions;
        IList<int> triangleIndices = geometry.TriangleIndices;

        var normals = new Vector3Collection(positions.Count);
        for (var i = 0; i < positions.Count; i++) {
            normals.Add(new Vector3());
        }

        for (var i = 0; i < triangleIndices.Count; i += 3) {
            var index0 = triangleIndices[i];
            var index1 = triangleIndices[i + 1];
            var index2 = triangleIndices[i + 2];
            var p0 = positions[index0];
            var p1 = positions[index1];
            var p2 = positions[index2];
            var u = p1 - p0;
            var v = p2 - p0;
            var w = Vector3.Cross(u, v);
            w.Normalize();
            normals[index0] += w;
            normals[index1] += w;
            normals[index2] += w;
        }

        for (var i = 0; i < normals.Count; i++) {
            var n = normals[i];
            n.Normalize();
            normals[i] = n;
        }

        return normals;
    }
}
