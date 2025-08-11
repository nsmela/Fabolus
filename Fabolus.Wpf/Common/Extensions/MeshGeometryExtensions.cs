using Fabolus.Core.Meshes;
using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Common.Extensions;

public static class MeshGeometryExtensions {
    public static bool IsEmpty(this MeshGeometry3D mesh) =>
        mesh is null || mesh.Positions is null || mesh.Positions.Count() == 0 || mesh.TriangleIndices.Count() == 0;

    public static MeshModel ToMeshModel(this MeshGeometry3D geometry) {
        var positions = geometry.Positions.Select(v => new System.Windows.Media.Media3D.Vector3D(v.X, v.Y, v.Z));
        var triangles = geometry.TriangleIndices;
        return new MeshModel(positions, triangles);
    }
}
