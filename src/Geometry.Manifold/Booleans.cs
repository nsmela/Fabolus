using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using MNManifold = ManifoldNET.Manifold;
using MNBoolOperation = ManifoldNET.BoolOperationType;

namespace GeometryManifold;

/// <summary>
/// Boolean operations, the one place Manifold most obviously earns its keep: its booleans are
/// exact and guaranteed to return a manifold solid, so unlike MeshLib's there is no error string
/// to inspect afterwards.
/// </summary>
internal sealed class Booleans : IBooleans
{
    private readonly IGeometryEngine _engine;

    public Booleans(IGeometryEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// MeshLib's BooleanOperation names leak into mesh metadata, and features read them back, so
    /// the names are kept verbatim rather than renamed to Manifold's vocabulary.
    /// </summary>
    private Result<IMesh> DoBoolean(IMesh meshA, IMesh meshB, MNBoolOperation operation, string operationName)
    {
        var manifoldA = meshA.ToManifold();
        if (manifoldA.IsFailure) return manifoldA.Error;

        var manifoldB = meshB.ToManifold();
        if (manifoldB.IsFailure)
        {
            manifoldA.Value.Dispose();
            return manifoldB.Error;
        }

        using var a = manifoldA.Value;
        using var b = manifoldB.Value;

        try
        {
            using var result = MNManifold.BooleanOperation(a, b, operation);

            var metadata = new MeshMetadata().WithProperties(m =>
                m.Set(CoreKeys.Id, Guid.NewGuid())
                 .Set(CoreKeys.Name, $"{meshA.Metadata.Name} {operationName} {meshB.Metadata.Name}")
                 .Set(CoreKeys.CreatedBy, "BooleanOperation"));

            return Result.Success(result.ToIMesh(metadata));
        }
        catch (Exception ex)
        {
            return new Error("MRBooleans.OperationFailed", ex.Message);
        }
    }

    public Result<IMesh> Intersect(IMesh meshA, IMesh meshB) =>
        DoBoolean(meshA, meshB, MNBoolOperation.Intersect, "Intersection");

    public Result<IMesh> Subtract(IMesh meshA, IMesh meshB) =>
        DoBoolean(meshA, meshB, MNBoolOperation.Subtract, "DifferenceAB");

    public Result<IMesh> Union(IMesh meshA, IMesh meshB) =>
        DoBoolean(meshA, meshB, MNBoolOperation.Add, "Union");
}
