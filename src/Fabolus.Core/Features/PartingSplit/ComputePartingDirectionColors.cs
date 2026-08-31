using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using System.Numerics;

namespace Fabolus.Core.Features.PartingSplit;

public class ComputePartingDirectionColors
{
    /// <summary>
    /// Classifies every <em>triangle</em> as facing along the pull direction (red), away from it
    /// (green), or draft-neutral (grey), from the angle between its own face normal and the pull
    /// direction. Returns interleaved RGB per triangle - length = TriangleCount * 3.
    ///
    /// <para>
    /// Per-face rather than per-vertex on purpose. A vertex normal is the average of the faces around
    /// it, so shading by it smears the classification across the boundary between an up-facing and a
    /// down-facing region and paints a soft gradient wherever the mesh is coarse - which is exactly
    /// where the user is trying to read the draft. Colouring each facet by its own normal shows the
    /// actual per-facet draft, so the neutral band reads as the set of facets that genuinely have no
    /// draft either way.
    /// </para>
    ///
    /// <para>
    /// The model's own rim is deliberately not shaded here. It is a feature of the shape rather than
    /// of the pull direction, and it is far too narrow to survive being expressed in whole facets -
    /// see <see cref="RidgeDetection.FindRidgeContours"/>, which draws it as a curve over the top of
    /// this shading instead.
    /// </para>
    ///
    /// <para>
    /// Note this is a different quantity from the one the parting line is traced against: the isoline
    /// walks the interpolated per-vertex normal field, so on a coarse mesh the line will not sit
    /// exactly on the band of neutral-coloured facets. The shading is honest about each facet; the
    /// line is the smooth silhouette through them.
    /// </para>
    /// </summary>
    public Result<double[]> Execute(IMesh baseMesh, PartingLineParameters parameters)
    {
        if (baseMesh is null)
            return MeshErrors.NullSource;
        if (parameters.PullDirection == Vector3.Zero)
            return MeshErrors.InvalidPullDirection;

        var vertices = baseMesh.Vertices;
        var triangles = baseMesh.Triangles;
        var direction = Vector3.Normalize(parameters.PullDirection);

        // The same band the geometry uses, so what the user sees shaded neutral is the same tolerance
        // that places the parting line. These were separate fields that only this shading read, which
        // is how the band came to be displayed but never applied.
        float upper = parameters.NeutralBand.Upper;
        float lower = parameters.NeutralBand.Lower;

        int triangleCount = triangles.Length / 3;
        var colors = new double[triangleCount * 3];

        for (int t = 0; t < triangleCount; t++)
        {
            var a = vertices[triangles[t * 3]];
            var b = vertices[triangles[(t * 3) + 1]];
            var c = vertices[triangles[(t * 3) + 2]];

            var normal = Vector3.Cross(b - a, c - a);
            // A degenerate face has no meaningful normal; grey is the honest answer, not a wrong one.
            float dot = normal.LengthSquared() < 1e-12f
                ? 0f
                : Vector3.Dot(Vector3.Normalize(normal), direction);

            int idx = t * 3;
            if (dot > upper)
            {
                colors[idx] = 1.0;     // R - faces along the pull direction
                colors[idx + 1] = 0.0;
                colors[idx + 2] = 0.0;
            }
            else if (dot < lower)
            {
                colors[idx] = 0.0;
                colors[idx + 1] = 1.0; // G - faces away from it
                colors[idx + 2] = 0.0;
            }
            else
            {
                colors[idx] = 0.8;     // grey - draft-neutral
                colors[idx + 1] = 0.8;
                colors[idx + 2] = 0.8;
            }
        }

        return colors;
    }
}
