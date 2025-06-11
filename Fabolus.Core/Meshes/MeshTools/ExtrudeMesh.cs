using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.MeshTools;
public static partial class MeshTools {

    public static DMesh3 ExtrudeMesh(DMesh3 mesh, double distance) {
        MeshExtrudeMesh extruder = new(mesh){
            ExtrudedPositionF = (position, normal, index) => new Vector3d(position.x, position.y, position.z + distance),
        };
        extruder.Extrude();
        return extruder.Mesh;

    }

    public static DMesh3 ExtrudePolygon(Polygon2d polygon, double minHeight, double maxHeight) {
        if (polygon is null || polygon.Vertices.Count < 3) { return new DMesh3(); }

        // Triangulate the polygon
        TriangulatedPolygonGenerator generator = new() {
            Polygon = new GeneralPolygon2d(polygon),
            Clockwise = false,
        };

        DMesh3 planarMesh = new();
        try {
            planarMesh = generator.Generate().MakeDMesh();
            if (planarMesh is null) {
                return new();
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error generating triangulated polygon: {ex.Message}");
            return new DMesh3();
        }

        // Extrude the mesh
        Vector3d z_distance = (maxHeight - minHeight) * Vector3d.AxisZ;
        MeshExtrudeMesh extruder = new(new DMesh3(planarMesh)) {
            ExtrudedPositionF = (Vector3d vert, Vector3f normal, int index) => vert + z_distance,
        };
        bool passed = extruder.Extrude();
        if (!passed) { return new DMesh3(); }

        MeshTransforms.Translate(extruder.Mesh, new Vector3d(0, 0, minHeight));

        return extruder.Mesh;
    }
}
