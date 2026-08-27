using HelixToolkit.Wpf.SharpDX;
using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using SharpDX;

namespace Fabolus.Wpf.Common.Mesh;

public static class MeshConverters {
    public static Result<MeshGeometry3D> ToHelixMesh(this IMesh mesh, IGeometryEngine engine, double[]? vertexColours = null) {
        var renderDataResult = engine.Evaluators.GetRenderData(mesh);
        if (renderDataResult.IsFailure)
            return renderDataResult.Error;

        var renderData = renderDataResult.Value;
        var geometry = new MeshGeometry3D();

        var positions = new Vector3Collection();
        for (int i = 0; i < renderData.Vertices.Length; i += 3) {
            positions.Add(new Vector3(
                (float)renderData.Vertices[i],
                (float)renderData.Vertices[i + 1],
                (float)renderData.Vertices[i + 2]));
        }
        geometry.Positions = positions;

        if (renderData.Normals is not null && renderData.Normals.Length > 0) {
            var normals = new Vector3Collection();
            for (int i = 0; i < renderData.Normals.Length; i += 3)
                normals.Add(new Vector3(
                    (float)renderData.Normals[i],
                    (float)renderData.Normals[i + 1],
                    (float)renderData.Normals[i + 2]));
            geometry.Normals = normals;
        }

        var colors = vertexColours ?? renderData.Colors;
        if (colors is not null && colors.Length > 0) {
            var colorCollection = new Color4Collection();
            for (int i = 0; i < colors.Length; i += 3)
                colorCollection.Add(new Color4((float)colors[i], (float)colors[i + 1], (float)colors[i + 2], 1.0f));
            geometry.Colors = colorCollection;
        }

        geometry.Indices = new IntCollection(renderData.Triangles);
        return geometry;
    }

    /// <summary>
    /// Builds display geometry with every triangle owning its own three vertices, so each face can
    /// carry its own flat colour and its own normal.
    ///
    /// <para>
    /// HelixToolkit's MeshGeometry3D has no per-face colour channel - Colors is indexed by vertex - so
    /// a shared vertex can only hold one colour however many faces meet at it. Un-welding is what makes
    /// per-triangle shading expressible at all. It costs 3x the vertices, which is why it's a separate
    /// converter rather than the default: only the direction-classification display needs it.
    /// </para>
    ///
    /// <para>
    /// <paramref name="triangleColours"/> is interleaved RGB per triangle (length = TriangleCount * 3),
    /// as produced by ComputePartingDirectionColors; each face's colour is written to all three of its
    /// corners, giving a hard edge between faces instead of a gradient across them.
    /// </para>
    /// </summary>
    public static Result<MeshGeometry3D> ToFlatShadedHelixMesh(
        this IMesh mesh, IGeometryEngine engine, double[]? triangleColours = null) {
        var renderDataResult = engine.Evaluators.GetRenderData(mesh);
        if (renderDataResult.IsFailure)
            return renderDataResult.Error;

        var renderData = renderDataResult.Value;
        var source = renderData.Vertices;
        var indices = renderData.Triangles;
        int triangleCount = indices.Length / 3;

        var positions = new Vector3Collection(triangleCount * 3);
        var normals = new Vector3Collection(triangleCount * 3);
        var colours = new Color4Collection(triangleCount * 3);
        var flatIndices = new IntCollection(triangleCount * 3);

        bool hasColours = triangleColours is not null && triangleColours.Length >= triangleCount * 3;

        for (int t = 0; t < triangleCount; t++) {
            var a = At(source, indices[t * 3]);
            var b = At(source, indices[(t * 3) + 1]);
            var c = At(source, indices[(t * 3) + 2]);

            var normal = Vector3.Cross(b - a, c - a);
            normal = normal.LengthSquared() < 1e-12f ? new Vector3(0, 1, 0) : Vector3.Normalize(normal);

            var colour = hasColours
                ? new Color4(
                    (float)triangleColours![t * 3],
                    (float)triangleColours[(t * 3) + 1],
                    (float)triangleColours[(t * 3) + 2],
                    1.0f)
                : new Color4(0.8f, 0.8f, 0.8f, 1.0f);

            positions.Add(a); positions.Add(b); positions.Add(c);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            colours.Add(colour); colours.Add(colour); colours.Add(colour);
            flatIndices.Add(t * 3); flatIndices.Add((t * 3) + 1); flatIndices.Add((t * 3) + 2);
        }

        return new MeshGeometry3D {
            Positions = positions,
            Normals = normals,
            Colors = colours,
            Indices = flatIndices,
        };

        static Vector3 At(double[] verts, int index) => new(
            (float)verts[index * 3], (float)verts[(index * 3) + 1], (float)verts[(index * 3) + 2]);
    }
}
