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
}
