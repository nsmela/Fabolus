using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Overhangs;

/// <summary>
/// Paints each vertex of a mesh with a gradient colour derived from how directly that
/// vertex faces the overhang direction. The direction and gradient are inputs, so the
/// same workflow serves any build orientation and any palette.
/// </summary>
public sealed class ComputeOverhangColors(IGeometryEngine Engine) {
    public Result<OverhangColoring> Execute(Workspace workspace, Guid meshId, OverhangSettings settings) {
        if (settings is null)
            return new Error("Overhang.NullSettings", "Overhang settings must be provided.");

        var span = settings.MaxAngleDegrees - settings.MinAngleDegrees;
        if (span <= 0f)
            return new Error("Overhang.InvalidAngleRange", "MaxAngleDegrees must be greater than MinAngleDegrees.");

        var meshResult = workspace.GetMesh(meshId);
        if (meshResult.IsFailure)
            return meshResult.Error;

        var normalsResult = Engine.Evaluators.ComputeVertexNormals(meshResult.Value);
        if (normalsResult.IsFailure)
            return normalsResult.Error;

        var normals = normalsResult.Value;
        var angles = new float[normals.Count];
        var colors = new double[normals.Count * 3];

        for (int i = 0; i < normals.Count; i++) {
            var angle = settings.Direction.AngleToDegrees(normals[i]);
            angles[i] = angle;

            var t = Math.Clamp((angle - settings.MinAngleDegrees) / span, 0f, 1f);
            var color = settings.Gradient.Sample(t);

            colors[i * 3] = color.R;
            colors[i * 3 + 1] = color.G;
            colors[i * 3 + 2] = color.B;
        }

        return new OverhangColoring(colors, angles);
    }
}