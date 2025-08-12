using g3;
using System.Numerics;

namespace Fabolus.Core.Meshes.MeshTools;

public static class DraftRegions {

    // to lazy to set up flags atm
    public enum DraftRegionClassification {
        Neutral,
        Positive,
        Negative,
        Occluded,
    }

    public static Dictionary<DraftRegionClassification, MeshModel> GenerateDraftMeshes(MeshModel model, Vector3 pull_direction, double tolerance) {
        DMesh3 mesh = model.Mesh;
        MeshNormals.QuickCompute(mesh);
        Vector3d dir = new Vector3d(pull_direction.X, pull_direction.Y, pull_direction.Z);

        // tolerance angles
        double upper_angle = 90.0 + tolerance;
        double lower_angle = 90.0 - tolerance;

        // classify each triangle
        DraftRegionClassification[] triangles = new DraftRegionClassification[mesh.TriangleCount];
        foreach (int tId in mesh.TriangleIndices()) {
            double angle_d = mesh.GetTriNormal(tId).AngleD(dir);
            triangles[tId] = angle_d switch {
                _ when angle_d > upper_angle => DraftRegionClassification.Positive,
                _ when angle_d < lower_angle => DraftRegionClassification.Negative,
                _ => DraftRegionClassification.Neutral
            };

        }

        // post processing
        CleanupRegions(ref triangles, mesh);
        CleanupRegions(ref triangles, mesh);
        bool[] occluded = OccludedTriangles(mesh, dir);

        // create a mesh for each classification
        DMesh3 positive_mesh = new DMesh3();
        DMesh3 negative_mesh = new DMesh3();
        DMesh3 neutral_mesh = new DMesh3();
        DMesh3 occluded_mesh = new DMesh3();

        // easier to ensure the mesh has all the vertices than to feed only the required ones
        // keeps triangle indices references consistent
        foreach(Vector3d v in mesh.Vertices()) {
            positive_mesh.AppendVertex(v);
            negative_mesh.AppendVertex(v);
            neutral_mesh.AppendVertex(v);
            occluded_mesh.AppendVertex(v);
        }

        // generate meshes for each draft region type
        for(int i =0; i < triangles.Length; i++) {
            if (occluded[i]) {
                occluded_mesh.AppendTriangle(mesh.GetTriangle(i));
                continue;
            }

            switch (triangles[i]) {
                case DraftRegionClassification.Positive:
                    positive_mesh.AppendTriangle(mesh.GetTriangle(i));
                    break;
                case DraftRegionClassification.Negative:
                    negative_mesh.AppendTriangle(mesh.GetTriangle(i));
                    break;
                default:
                    neutral_mesh.AppendTriangle(mesh.GetTriangle(i));
                    break;
            }
        }

        Dictionary<DraftRegionClassification, MeshModel> results = [];
        results[DraftRegionClassification.Positive] = new MeshModel(positive_mesh);
        results[DraftRegionClassification.Negative] = new MeshModel(negative_mesh);
        results[DraftRegionClassification.Neutral] = new MeshModel(neutral_mesh);
        results[DraftRegionClassification.Occluded] = new MeshModel(occluded_mesh);

        return results;
    }

    internal static void CleanupRegions(ref DraftRegionClassification[] regions, DMesh3 mesh) {
        // remove triangles surrounded by another classification
        for(int i = 0; i < regions.Length; i++) {
            DraftRegionClassification c = regions[i];
            Index3i tris = mesh.GetTriNeighbourTris(i);
            bool match_a = regions[tris.a] == c;
            bool match_b = regions[tris.b] == c;
            bool match_c = regions[tris.c] == c;
            if (match_a || match_b || match_c) { continue; }

            if (regions[tris.a] == regions[tris.b]  || regions[tris.a] == regions[tris.c]) {
                regions[i] = regions[tris.a];
                continue;
            }

            regions[i] = regions[tris.b];
        }

        // fill triangles with two matching neighbours
        for (int i = 0; i < regions.Length; i++) {
            DraftRegionClassification draft = regions[i];
            Index3i tris = mesh.GetTriNeighbourTris(i);
            int a = tris.a;
            int b = tris.b;
            int c = tris.c;

            int count = 0;
            if (regions[a] == draft) { count++; }
            if (regions[b] == draft) { count++; }
            if (regions[c] == draft) { count++; }

            if (count == 1) {
                if (regions[a] == regions[b] || regions[a] == regions[c]) {
                    regions[i] = regions[a];
                    continue;
                } else {
                    regions[i] = regions[b];
                    continue;
                }
            }
        }
    }

    internal static bool[] OccludedTriangles(DMesh3 mesh, Vector3d direction) {
        DMeshAABBTree3 spatial = new(mesh, autoBuild: true);
        bool[] occluded = new bool[mesh.TriangleCount];

        foreach (int tId in mesh.TriangleIndices()) {
            occluded[tId] = IsOccuded(mesh, spatial, tId, direction);
        }

        return occluded;
    }

    internal static bool IsOccuded(DMesh3 mesh, DMeshAABBTree3 spatial, int tId, Vector3d direction) {
        const double epsilon = 1e-6;

        Index3i triangle = mesh.GetTriangle(tId);
        Vector3d[] points = [
            mesh.GetVertex(triangle.a),
            mesh.GetVertex(triangle.b),
            mesh.GetVertex(triangle.c),
        ];

        Vector3d normal = mesh.GetTriNormal(tId);

        // flip direction if triangle is facing away from direction
        if (normal.AngleD(direction) > 90) {
            direction *= -1;
        }

        foreach (Vector3d p in points) {
            Ray3d origin = new(p + direction * epsilon, direction);
            int hitId = spatial.FindNearestHitTriangle(origin);
            if ( (hitId == DMesh3.InvalidID || hitId == tId)) { continue; }

            return true;
        }

        return false;
    }
}
