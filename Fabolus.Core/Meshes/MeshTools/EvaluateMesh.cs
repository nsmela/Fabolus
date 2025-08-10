using g3;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {
    // ref: https://github.com/MeshInspector/MeshLib/discussions/4921

    // MRMeshCollide findSelfCollidingTriangles
    // MRMeshFixer findDisorientedFaces
    // MRRegionBoundry findRightBoundary

    public static MeshError[] EvaluateMesh(MeshModel model) {
        DMesh3 mesh = model.Mesh;
        DMesh3 copy = new();
        copy.Copy(mesh, true, false, false);

        // detect self-intersections
        List<int> intersection_triangles = [];
        foreach(int tId in mesh.TriangleIndices()) {
            Index3i tA = mesh.GetTriangle(tId);
            Triangle3d triA = new(
                mesh.GetVertex(tA.a),
                mesh.GetVertex(tA.b),
                mesh.GetVertex(tA.c)
            );
            foreach (int tId_other in mesh.TriangleIndices().Where(i => i != tId)) {
                Index3i tB = mesh.GetTriangle(tId_other);
                Triangle3d triB = new(
                    mesh.GetVertex(tB.a),
                    mesh.GetVertex(tB.b),
                    mesh.GetVertex(tB.c)
                );

                if (IntrTriangle3Triangle3.Intersects(ref triA, ref triB)) {
                    intersection_triangles.Add(tId_other);
                }

            }
        }

        // detect suspicious or defective orientation

        // detect holes
        
        return [MeshError.NONE];
    }

}

