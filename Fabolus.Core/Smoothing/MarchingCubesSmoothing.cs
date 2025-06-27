using Fabolus.Core.BolusModel;
using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes.MeshTools;
using g3;
using gs;
using System.Windows;
using static MR.DotNet;

namespace Fabolus.Core.Smoothing;

public record struct MarchingCubesSettings(float DeflateDistance, float InflateDistance, int Iterations, double CellSize);

public class MarchingCubesSmoothing {

    public static Bolus Smooth(Bolus bolus, MarchingCubesSettings settings) {
        float deflateDistance = settings.DeflateDistance;
        float inflateDistance = settings.InflateDistance;
        int iterations = settings.Iterations;
        float cell_size = (float)settings.CellSize;

        //shrink mesh to lose sharp details and inflate back to original size
        Mesh model = bolus.Mesh;
        int triangleCount = model.ValidFaces.Count();

        if (deflateDistance > 0) {
            for (int i = 0; i < iterations; i++) {
                //model = MeshTools.OffsetDouble(model, deflateDistance, cell_size);
                model = MeshTools.OffsetMesh(model, deflateDistance, cell_size);
                model = MeshTools.OffsetMesh(model, -deflateDistance, cell_size);
            }
        }

        Mesh smoothed = MeshTools.OffsetMesh(model, inflateDistance);

        smoothed = MeshTools.Resize(smoothed, triangleCount * 2);

        return new(new Meshes.MeshModel(smoothed));
    }

}
