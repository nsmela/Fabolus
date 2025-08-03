using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {
    public static DMesh3 ExtrudeMesh(DMesh3 mesh, Vector3d direction, double distance) {
        // copy to ensure we don't change the original mesh
        DMesh3 mA = new();
        mA.Copy(mesh);

        DMesh3 mB = new();
        mB.Copy(mesh);

        DMesh3 extruded = new();
        extruded.Copy(copy: mA, bNormals: true, bColors: false, bUVs: false);
        MeshTransforms.Translate(extruded, direction.Normalized * distance);

        MeshEditor editor = new(mB);
        editor.ReverseTriangles(editor.Mesh.TriangleIndices());
        editor.AppendMesh(extruded);

        // TODO: stitch edges
        var loops = new MeshBoundaryLoops(editor.Mesh);
        EdgeLoop loopA = loops[0];

        EdgeLoop loopB = default(EdgeLoop);
        for (int i = 1; i < loops.Count(); i++) {
             if (loops[i].VertexCount != loopA.VertexCount || loops[i].Vertices[i] == loopA.Vertices[0]) {
                continue;
             }

            loopB = loops[i];
            break;
        }


        // issue with loops
        if (loopB is null) {
            return editor.Mesh;
        }

        // TODO: figure out why this is returning -2, which means the appended triangle was not appended to a boundry edge
        int[] new_tris = StitchLoop(editor.Mesh, loopA.Vertices, loopB.Vertices);
        
        // TODO: cleanup and repair

        return editor.Mesh;
    }
    private static int[] StitchLoop(DMesh3 mesh, int[] vloop1, int[] vloop2, int group_id = -1) {
        int num = vloop1.Length;
        if (num != vloop2.Length) {
            throw new Exception("MeshEditor.StitchLoop: loops are not the same length!!");
        }

        int[] array = new int[num * 2];
        int num2 = 0;
        while (true) {
            if (num2 < num) {
                int num3 = vloop1[num2];
                int ii = vloop1[(num2 + 1) % num];
                int jj = vloop2[num2];
                int kk = vloop2[(num2 + 1) % num];
                Index3i tv = new Index3i(ii, num3, kk);
                Index3i tv2 = new Index3i(num3, jj, kk);
                int num4 = mesh.AppendTriangle(tv, group_id);
                int num5 = mesh.AppendTriangle(tv2, group_id);
                array[2 * num2] = num4;
                array[2 * num2 + 1] = num5;
                if (num4 < 0 || num5 < 0) {
                    break;
                }

                num2++;
                continue;
            }

            return array;
        }

        return null;
    }

}
