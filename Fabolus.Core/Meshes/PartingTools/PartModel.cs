using Fabolus.Core.Meshes.MeshTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static MR.DotNet;
using static MR.DotNet.MeshComponents;

namespace Fabolus.Core.Meshes.PartingTools;

public static partial class PartingTools {

    public static Result<CuttingMeshResults> PartModel(CuttingMeshResults results) {
        Mesh cutting = results.CuttingMesh;
        Mesh mould = results.Mould;

        try {
            var meshes = MeshTools.MeshTools.BooleanSplit(mould, cutting, results.GapDistance);

            if (meshes.IsFailure) { return meshes.Errors; }
            if (meshes.Data.Length == 0) { return new MeshError($"Boolean Split produced no meshes!"); }
            if (meshes.Data.Length == 1) { return new MeshError($"Boolean Split produced only one mesh!"); }
            
            // determine which is the positive and which is the negative
            if (meshes.Data[0].BoundsLower()[1] > meshes.Data[1].BoundsLower()[1]) {
                results.PositivePullMesh = new MeshModel((Mesh)meshes.Data[0]);
                results.NegativePullMesh = new MeshModel((Mesh)meshes.Data[1]);
            } else {
                results.PositivePullMesh = new MeshModel((Mesh)meshes.Data[1]);
                results.NegativePullMesh = new MeshModel((Mesh)meshes.Data[0]);
            }

            results.PositivePullMesh.ApplyTranslation(0, results.GapDistance, 0);

            return results;
        }
        catch (Exception e) {
            return new MeshError($"Part Model failed: {e.Message}");
        }
    }
}
