using Fabolus.Core.BolusModel;
using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.MeshTools;
using g3;
using gs;
using System;
using System.Windows;
using static MR.DotNet;

namespace Fabolus.Core.Smoothing;

public record struct WeightedOffsetSettings(float InflateDistance, float WeightValue, float SmoothingAngleDegs, double CellSize);

public class WeightedOffsetSmoothing {

    public static MeshModel Smooth(Bolus bolus, WeightedOffsetSettings settings) {
        float inflateDistance = settings.InflateDistance;
        float smoothAngleRads = (float)Math.PI * settings.SmoothingAngleDegs / 180.0f;
        float cell_size = (float)settings.CellSize;
        float weightedValue = settings.WeightValue;

        // find largest two smoothed surfaces as these represent the area we don't want to affect
        var surfaces = MeshTools.GetSmoothSurfaces(bolus.Mesh, smoothAngleRads);

        // populate the weighted array
        int[] indexes = surfaces[0].TriangleIndices().Concat(surfaces[1].TriangleIndices()).ToArray();
        float[] weights = Enumerable.Repeat<float>(1.0f, bolus.Mesh.Mesh.TriangleCount).ToArray();
        int index = -1;
        for (int i = 0; i < indexes.Length; i ++) {
            index = indexes[i];
            weights[index] = weightedValue;
        }

        // apply weighted offset
        return MeshTools.WeightedOffsetMesh(bolus.Mesh, weights, inflateDistance, cell_size);
    }

}

