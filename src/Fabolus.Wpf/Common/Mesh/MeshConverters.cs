using HelixToolkit.Wpf.SharpDX;
using BasicResults;
using Fabolus.Core.Geometry;
using SharpDX;

namespace Fabolus.Wpf.Common.Mesh;

public static class MeshConverters {
    public static Result<MeshGeometry3D> ToHelixMesh(this IMesh mesh, IGeometryEngine engine, double[]? vertexColours = null) {
        var geometry = new MeshGeometry3D();

        var positions = new Vector3Collection(mesh.VertexCount);
        foreach (var v in mesh.Vertices) {
            positions.Add(new SharpDX.Vector3((float)v.X, (float)v.Y, (float)v.Z));
        }
        geometry.Positions = positions;

        var normalsResult = engine.Evaluators.ComputeVertexNormals(mesh);
        if (normalsResult.IsSuccess) {
            var normals = new Vector3Collection(mesh.VertexCount);
            foreach (var n in normalsResult.Value) {
                normals.Add(new SharpDX.Vector3((float)n.X, (float)n.Y, (float)n.Z));
            }
            geometry.Normals = normals;
        }

        if (vertexColours is not null && vertexColours.Length >= mesh.VertexCount * 3) {
            var colorCollection = new Color4Collection(mesh.VertexCount);
            for (int i = 0; i < mesh.VertexCount; i++) {
                colorCollection.Add(new SharpDX.Color4((float)vertexColours[i*3], (float)vertexColours[i*3+1], (float)vertexColours[i*3+2], 1.0f));
            }
            geometry.Colors = colorCollection;
        }

        var indices = new IntCollection(mesh.TriangleCount * 3);
        foreach (var t in mesh.Triangles) {
            indices.Add(t);
        }
        geometry.Indices = indices;
        return geometry;
    }
}
