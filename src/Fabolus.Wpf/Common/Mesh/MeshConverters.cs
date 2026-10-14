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

    /// <summary>
    /// The mesh with every triangle given its own three corners and its face normal, so each one
    /// shades flat and takes its own colour - which is what a per-face reading such as draft angle
    /// needs, where a shared vertex would blend its neighbours' colours across the edge.
    /// <paramref name="triangleColours"/> is RGB per triangle; without it every face is grey.
    /// </summary>
    public static MeshGeometry3D ToFlatShadedHelixMesh(this IMesh mesh, double[]? triangleColours = null) {
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;
        int triangleCount = triangles.Length / 3;

        var positions = new Vector3Collection(triangleCount * 3);
        var normals = new Vector3Collection(triangleCount * 3);
        var colours = new Color4Collection(triangleCount * 3);
        var indices = new IntCollection(triangleCount * 3);

        bool hasColours = triangleColours is not null && triangleColours.Length >= triangleCount * 3;

        for (int t = 0; t < triangleCount; t++) {
            var a = vertices[triangles[t * 3]];
            var b = vertices[triangles[(t * 3) + 1]];
            var c = vertices[triangles[(t * 3) + 2]];

            var cross = (b - a).Cross(c - a);
            var normal = cross.LengthSquared < 1e-12
                ? new SharpDX.Vector3(0, 1, 0)
                : new SharpDX.Vector3((float)(cross.X / cross.Length), (float)(cross.Y / cross.Length), (float)(cross.Z / cross.Length));

            var colour = hasColours
                ? new Color4((float)triangleColours![t * 3], (float)triangleColours[(t * 3) + 1], (float)triangleColours[(t * 3) + 2], 1.0f)
                : new Color4(0.8f, 0.8f, 0.8f, 1.0f);

            foreach (var corner in new[] { a, b, c }) {
                positions.Add(new SharpDX.Vector3((float)corner.X, (float)corner.Y, (float)corner.Z));
                normals.Add(normal);
                colours.Add(colour);
                indices.Add(positions.Count - 1);
            }
        }

        return new MeshGeometry3D {
            Positions = positions,
            Normals = normals,
            Colors = colours,
            Indices = indices,
        };
    }
}
