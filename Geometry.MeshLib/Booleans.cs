using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace GeometryMeshLib;

internal sealed class Booleans : IBooleans
{
    private Result<IMesh> DoBoolean(IMesh meshA, IMesh meshB, MR.BooleanOperation op)
    {
        if (meshA is not MRMesh mrA || meshB is not MRMesh mrB)
            return GeometryErrors.InvalidMeshType;

        var result = MR.boolean(mrA.Mesh, mrB.Mesh, op, null!);

        if (result is null)
            return new Error("MRBooleans.OperationFailed", "Boolean operation returned null result.");

        if (!string.IsNullOrEmpty(result.errorString))
        {
            return new Error("MRBooleans.OperationFailed", result.errorString);
        }

        if (result.mesh is null)
        {
            return new Error("MRBooleans.OperationFailed", "Boolean operation produced no mesh.");
        }

        var metadata = new MeshMetadata().WithProperties(m =>
            m.Set(CoreKeys.Id, Guid.NewGuid())
             .Set(CoreKeys.Name, $"{meshA.Metadata.Name} {op} {meshB.Metadata.Name}")
             .Set(CoreKeys.CreatedBy, "BooleanOperation"));

        return new MRMesh(result.mesh, metadata);
    }

    public Result<IMesh> Intersect(IMesh meshA, IMesh meshB) =>
        DoBoolean(meshA, meshB, MR.BooleanOperation.Intersection);

    public Result<IMesh> Subtract(IMesh meshA, IMesh meshB) =>
        DoBoolean(meshA, meshB, MR.BooleanOperation.DifferenceAB);

    public Result<IMesh> Union(IMesh meshA, IMesh meshB) =>
        DoBoolean(meshA, meshB, MR.BooleanOperation.Union);
}
