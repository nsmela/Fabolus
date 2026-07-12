using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using System.Numerics;

namespace Fabolus.Core.Features.PartingSplit;

public class ComputePartingDirectionColors
{
    private readonly IGeometryEngine _engine;

    public ComputePartingDirectionColors(IGeometryEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Computes an RGB color for every vertex based on the dot product between
    /// the vertex normal and the parting direction.
    /// Positive (facing direction) = red, Negative = green.
    /// </summary>
    public Result<double[]> Execute(IMesh baseMesh, Vector3 pullDirection)
    {
        var normalsResult = _engine.Evaluators.ComputeVertexNormals(baseMesh);
        if (normalsResult.IsFailure)
            return normalsResult.Error;

        var normals = normalsResult.Value;
        var colors = new double[normals.Count * 3];
        var direction = Vector3.Normalize(pullDirection);

        for (int i = 0; i < normals.Count; i++)
        {
            float dot = Vector3.Dot(normals[i], direction);
            int idx = i * 3;

            if (dot > 0.01f)
            {
                colors[idx] = 1.0;     // R
                colors[idx + 1] = 0.0; // G
                colors[idx + 2] = 0.0; // B
            }
            else if (dot < -0.01f)
            {
                colors[idx] = 0.0;     // R
                colors[idx + 1] = 1.0; // G
                colors[idx + 2] = 0.0; // B
            }
            else
            {
                // Tangent / Neutral
                colors[idx] = 0.8;     // R
                colors[idx + 1] = 0.8; // G
                colors[idx + 2] = 0.8; // B
            }
        }

        return colors;
    }
}
