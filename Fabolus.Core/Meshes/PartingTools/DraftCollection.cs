using System.Numerics;
using g3;
using Fabolus.Core.Extensions;

namespace Fabolus.Core.Meshes.PartingTools;

public static partial class PartingTools {
    public sealed class DraftCollection(DMesh3 mesh) : Dictionary<int, DraftClassification> {
        public readonly DMesh3 Mesh = mesh;
        public int[] GetTriangleNeighbours(int tId) =>
            Mesh.GetTriNeighbourTris(tId).array;
    }

    public static IEnumerable<int> GetDraftRegion(this DraftCollection collection, DraftClassification classification) =>
        collection
            .Where(x => x.Value == classification)
            .Select(x => x.Key);

    public enum DraftClassification {
        INVALID, // has no neighbours
        POSITIVE, // angle is positive, i.e. draft angle is away from the parting line
        NEGATIVE, // angle is negative, i.e. draft angle is towards the parting line
        NEUTRAL, // angle is zero, i.e. draft angle is perpendicular to the parting line
    }

    public static DraftCollection GenerateDraftCollection(MeshModel model, Vector3 pullDirection, double neutralThresholdDeg = 5.0) {
        Vector3d direction = pullDirection.ToVector3d().Normalized;
        double threshold = Math.PI / 180.0 * neutralThresholdDeg;
        DraftCollection results = new(new(model));

        Vector3d normal;
        double angle;
        foreach (int tId in results.Mesh.TriangleIndices()) {
            normal = results.Mesh.GetTriNormal(tId);
            angle = normal.AngleR(direction);
            var classification = ClassifyDraftAngle(angle, threshold);

            results.Add(tId, classification);
        }

        DraftCollectionRefining(ref results);
        DraftCollectionRefining(ref results);

        return results;
    }

    private static DraftClassification ClassifyDraftAngle(double angleRads, double neutralThresholdRads) {
        const double ninetyDegreesRads = Math.PI / 2.0;
        if (angleRads < ninetyDegreesRads - neutralThresholdRads) { return DraftClassification.POSITIVE; }
        if (angleRads > ninetyDegreesRads + neutralThresholdRads) { return DraftClassification.NEGATIVE; }
        return DraftClassification.NEUTRAL;
    }

    private static void DraftCollectionRefining(ref DraftCollection drafts) {
        // first pass tp check if triangle is not connected to anything and add it to neutral if so
        foreach (var (tId, classification) in drafts) {
            MatchNeighbours(ref drafts, tId);
        }

    }

    /// <summary>
    /// Checks if the referenced triangle's classification matches all triangle ids 
    /// </summary>
    /// <param name="drafts"></param>
    /// <param name="refId"></param>
    /// <param name="values"></param>
    /// <returns></returns>
    public static bool CompareClassifications(this DraftCollection drafts, int refId, params int[] values) {
        // try to fail fast
        foreach (int id in values) {
            if (drafts[refId] != drafts[id]) { return false; }
        }

        return true;
    }

    public static DraftClassification GetCommonClassification(this DraftCollection drafts, int[] values) {
        if (drafts[values[0]] == drafts[values[1]] &&
            drafts[values[0]] == drafts[values[2]]) {  return drafts[values[0]]; }

        if (drafts[values[1]] == drafts[values[2]]) { return drafts[values[1]]; }

        return DraftClassification.NEUTRAL;
    }

    public static void MatchNeighbours(ref DraftCollection drafts, int tId) {
        int[] neighbours = drafts.GetTriangleNeighbours(tId);
        
        // something's wrong, the neighbours' id is wrong
        if (neighbours[0] == -1 ||  neighbours[1] == -1 || neighbours[2] == -1) {
            drafts[tId] = DraftClassification.INVALID;
            return;
        }

        DraftClassification c = drafts[tId];
        var (n0, n1, n2) = (drafts[neighbours[0]], drafts[neighbours[1]], drafts[neighbours[2]]); // easier to read later

        // check if all neighbours match each other
        if (n0 == n1 && n0 == n2) {
            drafts[tId] = n0;
            return;
        }

        // if neutral and two or more neighbours are negative
        if (c == DraftClassification.NEUTRAL && 
                n0 == DraftClassification.NEGATIVE && n1 == DraftClassification.NEGATIVE ||
                n0 == DraftClassification.NEGATIVE && n2 == DraftClassification.NEGATIVE ||
                n1 == DraftClassification.NEGATIVE && n2 == DraftClassification.NEGATIVE) {
            drafts[tId]= DraftClassification.NEGATIVE;
            return;
        }

        // if negative and only one other negative neighbour
        if (c == DraftClassification.NEGATIVE) {
            int neg_count = 0;
            if (n0 == DraftClassification.NEGATIVE) { neg_count++; }
            if (n1 == DraftClassification.NEGATIVE) { neg_count++; }
            if (n2 == DraftClassification.NEGATIVE) { neg_count++; }

            if (neg_count == 1) {
                drafts[tId] = DraftClassification.NEUTRAL;
                return;
            }
        }

    }
}
