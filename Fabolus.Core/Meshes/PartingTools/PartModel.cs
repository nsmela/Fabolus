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
               
            results.PositivePullMesh = new MeshModel((Mesh)meshes.Data[0]);
            results.NegativePullMesh = new MeshModel((Mesh)meshes.Data[1]);
            
        }
        catch (Exception e) {
            return new MeshError($"Part Model failed: {e.Message}");
        }

        return results;
    }
}
