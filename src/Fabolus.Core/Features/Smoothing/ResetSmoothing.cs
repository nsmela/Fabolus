using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Smoothing;

public sealed class ResetSmoothing {
    private readonly IGeometryEngine _engine;

    public ResetSmoothing(IGeometryEngine engine) {
        _engine = engine;
    }

    public Result<Workspace> Execute(Workspace workspace) {
        var getMeshResult = workspace.GetActiveMesh();
        if (getMeshResult.IsFailure) return getMeshResult.Error;

        var activeMesh = getMeshResult.Value;

        var smoothResult = activeMesh.Metadata.GetSmoothing();
        if (smoothResult.HasNoValue) return workspace;
        var settings = smoothResult.Value;

        var currentId = activeMesh.Metadata.Id;
        var derivedResult = activeMesh.Metadata.DerivedFrom;
        if (derivedResult.HasNoValue) return SmoothMeshErrors.NoDerived;

        var derivedId = derivedResult.Value;
        var activeResult = workspace.SetActiveMesh(derivedId);
        if (activeResult.IsFailure) {
            return activeResult.Error;
        }

        workspace = activeResult.Value;
        var id = currentId;
        return workspace.RemoveMesh(id);
    }
}

public static class SmoothMeshErrors {
    public static readonly Error NoSmoothing = new("Smoothing.None", "The active mesh is not smoothed.");
    public static readonly Error NoDerived = new("Smoothing.NoOriginal", "The smoothed mesh has no parent mesh!");
}
