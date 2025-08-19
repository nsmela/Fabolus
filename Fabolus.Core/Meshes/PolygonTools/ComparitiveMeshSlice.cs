using Fabolus.Core.Extensions;
using NetTopologySuite.Geometries;
using g3;
using NetTopologySuite.Operation.Union;
using Clipper2Lib;


namespace Fabolus.Core.Meshes.PolygonTools;

public static partial class PolygonTools {
    public record ComparitivePolygon {
        public required MeshModel UnionMesh { get; init; }
        public required MeshModel BodyMesh { get; init; }
        public required MeshModel ToolMesh { get; init; }
        public required double Height { get; init; }

    }


    public static ComparitivePolygon ComparativeMeshSlice(DMesh3 body, DMesh3 tool, double height) {
        const int precision = 4;
        const FillRule fillRule = FillRule.EvenOdd;

        var body_slices = MeshSlice(body, height);
        var tool_slices = MeshSlice(tool, height);

        PathsD body_paths = new(body_slices.Select(p => p.ToPathD()));
        PathsD tool_paths = new(tool_slices.Select(p => p.ToPathD()));

        PathsD solution = Clipper.Intersect(tool_paths, body_paths, fillRule, precision);
        var union_mesh = solution.ToMesh();
        MeshTransforms.Translate(union_mesh, Vector3d.AxisZ * height);

        var body_mesh = body_paths.ToMesh();
        MeshTransforms.Translate(body_mesh, Vector3d.AxisZ * (height - 0.1));

        var tool_mesh = tool_paths.ToMesh();
        MeshTransforms.Translate(tool_mesh, Vector3d.AxisZ * (height - 0.2));

        return new() {
            BodyMesh = new MeshModel(body_mesh),
            ToolMesh = new MeshModel(tool_mesh),
            UnionMesh = new MeshModel(union_mesh),
            Height = height
        };
    }

    public static Polygon2d[] MeshSlice(DMesh3 mesh, double height) {
        var cut_mesh = new DMesh3(mesh);  // slicing can modify the mesh, so we work on a copy

        // capture edge loops at the height and returns them as polygons
        List<Polygon2d> polygons = new();
        foreach (var loop in CutMesh(cut_mesh, height)) {
            if (loop.Vertices.Length < 3) { continue; } // Skip loops with less than 3 points

            DCurve3 border_curve = loop.ToCurve(cut_mesh);
            polygons.Add(new(border_curve.Vertices.Select(v => new Vector2d(v.x, v.y))));
        }

        return polygons.ToArray();
    }

    private static IEnumerable<EdgeLoop> CutMesh(DMesh3 mesh, double z_height) {
        if (mesh.IsEmpty()) { throw new ArgumentException("No mesh provided to cut."); }
        MeshPlaneCut cutter = new(
            mesh,
            new Vector3d(0, 0, z_height),
            Vector3d.AxisZ
        );
        bool successful = cutter.Cut();
        if (!successful) { throw new InvalidOperationException("Failed to contour the mesh at the specified height."); }

        foreach (EdgeLoop edgeLoop in cutter.CutLoops) {
            yield return edgeLoop;
        }
    }

    private static Polygon ToPolygon(this Polygon2d polygon, GeometryFactory? factory = null) {
        if (factory is null) { factory = new(); }
        List<Coordinate> coords = polygon.Vertices.Select(v => new Coordinate(v.x, v.y)).ToList();
        coords.Add(coords[0]); // Ensure the polygon is closed
        return factory.CreatePolygon(coords.ToArray());
    }

    private static Polygon2d ToPolygon2d(this Polygon polygon) {
        var result = new Polygon2d(polygon.Coordinates.Select(c => new Vector2d(c.X, c.Y)));
        result = Resample(result);
        return result;
    }

    private static DMesh3 ToMesh(this Polygon polygon) {
        Polygon2d poly = polygon.ToPolygon2d();
        return poly.ToMesh();
    }

    private static DMesh3 ToMesh(this Polygon2d polygon) {
        TriangulatedPolygonGenerator generator = new() {
            Polygon = new GeneralPolygon2d(polygon),
            Clockwise = false,
        };
        return generator.Generate().MakeDMesh();
    }

    private static DMesh3 ToMesh(this MultiPolygon polygons) {
        if (polygons is null || polygons.IsEmpty) { return new DMesh3(); }

        MeshEditor editor = new(new DMesh3());
        foreach (var polygon in polygons.Geometries) {
            Polygon poly = polygon as Polygon;
            if (poly is null || poly.IsEmpty) { continue; }
            var mesh = poly.ToMesh();
            editor.AppendMesh(mesh);
        }

        return editor.Mesh;
    }

    private static PointD ToPointD(this Vector2d vector) => new PointD(vector.x, vector.y);
    private static PathD ToPathD(this Polygon2d polygon) => new(polygon.Vertices.Select(v => v.ToPointD()));
    private static Polygon2d ToPolygon2d(this PathD path) {
        if (path.Count == 0) { return new Polygon2d(); }
        var verts = path.Select(c => new Vector2d(c.x, c.y));
        return new Polygon2d(verts);
    }
    private static DMesh3 ToMesh(this PathsD paths) {
        MeshEditor editor = new(new DMesh3());
        foreach(var path in paths) {
            editor.AppendMesh(path.ToPolygon2d().ToMesh());
        }

        return editor.Mesh;
    }
}
