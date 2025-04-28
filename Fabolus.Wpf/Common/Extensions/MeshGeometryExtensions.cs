using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Common.Extensions;

public static class MeshGeometryExtensions {
    public static bool IsEmpty(this MeshGeometry3D mesh) =>
        mesh is null || mesh.Positions.Count() == 0 || mesh.TriangleIndices.Count() == 0;
}
