using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {
    public enum DraftClassification {
        POSITIVE, // angle is positive, i.e. draft angle is away from the parting line
        NEGATIVE, // angle is negative, i.e. draft angle is towards the parting line
        NEUTRAL, // angle is zero, i.e. draft angle is perpendicular to the parting line
    }

    public record struct DraftResult(int TriangleId, double AngleRads, DraftClassification Classification);

    public static Dictionary<int, DraftClassification> DraftAngleAnalysis(MeshModel model, double[] pullDirection, double neutralThresholdDeg = 5.0) {
        DMesh3 mesh = new (model.Mesh);
        Vector3d direction = new Vector3d(pullDirection[0], pullDirection[1], pullDirection[2]).Normalized;
        double threshold = Math.PI / 180.0 * neutralThresholdDeg;
        Dictionary<int, DraftClassification> results = [];

        Vector3d normal;
        double angle;
        foreach (int tId in mesh.TriangleIndices()) {

            normal = mesh.GetTriNormal(tId);
            angle = normal.AngleR(direction);
            var classification = ClassifyDraftAngle(angle, threshold);

            results.Add(tId, classification);
        }

        return results;
    }

    private static DraftClassification ClassifyDraftAngle(double angleRads, double neutralThresholdRads) {
        const double ninetyDegreesRads = Math.PI / 2.0;
        if (angleRads < ninetyDegreesRads - neutralThresholdRads) { return DraftClassification.POSITIVE; }
        if (angleRads > ninetyDegreesRads + neutralThresholdRads) { return DraftClassification.NEGATIVE; }
        return DraftClassification.NEUTRAL;
    }


}
