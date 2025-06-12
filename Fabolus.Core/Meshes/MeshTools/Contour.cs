using g3;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {
    public static MeshModel Contour(MeshModel model, double z_height) {
        MeshPlaneCut cutter = new(
            model.Mesh,
            new Vector3d(0, 0, z_height),
            Vector3d.AxisZ
        );

        bool successful = cutter.Cut();

        if (!successful) { return new MeshModel(); }
        if (cutter.CutLoops.Count() == 0) { return new MeshModel(); }

        MeshEditor editor = new(new DMesh3());
        foreach (EdgeLoop loop in cutter.CutLoops) {
            if (loop.Vertices.Length < 3) { continue; }// Cannot triangulate loops with less than 3 vertices

            var verts = loop.Vertices.Select(i => loop.Mesh.GetVertex(i));
            Polygon2d polygon = new(verts.Select(v => new Vector2d(v.x, v.y)));

            // Triangulation generally works best with Counter-Clockwise (CCW) polygons.
            // MeshPlaneCut usually provides CCW loops, but it's good practice to ensure it.
            if (polygon.IsClockwise) { polygon.Reverse(); }

            TriangulatedPolygonGenerator generator = new() {
                Polygon = new GeneralPolygon2d(polygon),
                Clockwise = false,
            };
            try {
                var contour = generator.Generate().MakeDMesh();
                MeshTransforms.Translate(contour, Vector3d.AxisZ * z_height);
                editor.AppendMesh(contour);
            } catch(Exception ex) {
                Console.WriteLine($"Contouring failed: {ex.Message}");
            }
        }
        return new MeshModel(editor.Mesh);
    }

}
