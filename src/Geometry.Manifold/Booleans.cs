using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using GeometryManifold.Internal.Native;

namespace GeometryManifold;

/// <summary>
/// Boolean operations, the one place Manifold most obviously earns its keep: they are exact and
/// the result is a closed solid by construction, so unlike MeshLib's there is no error string to
/// inspect afterwards.
/// </summary>
internal sealed class Booleans : IBooleans
{
    private readonly IGeometryEngine _engine;

    public Booleans(IGeometryEngine engine)
    {
        _engine = engine;
    }

    private delegate Result<ManifoldOutcome> Operation(IMesh left, IMesh right, MeshMetadata metadata);

    /// <summary>
    /// MeshLib's BooleanOperation names leak into mesh metadata and features read them back, so
    /// the names are kept verbatim rather than renamed to Manifold's vocabulary.
    /// </summary>
    private static Result<IMesh> DoBoolean(IMesh meshA, IMesh meshB, string operationName, Operation operation)
    {
        if (meshA is null || meshB is null) return GeometryErrors.NullMesh;

        var metadata = new MeshMetadata().WithProperties(m =>
            m.Set(CoreKeys.Id, Guid.NewGuid())
             .Set(CoreKeys.Name, $"{meshA.Metadata.Name} {operationName} {meshB.Metadata.Name}")
             .Set(CoreKeys.CreatedBy, "BooleanOperation"));

        var result = operation(meshA, meshB, metadata);
        if (result.IsFailure) return result.Error;

        // Whether an operand had to be welded on the way in is recorded, not swallowed: the
        // merge closes the mesh but moves geometry to do it, and a caller sending the result to
        // a printer should be able to tell that apart from an untouched boolean.
        if (result.Value.Provenance == ManifoldProvenance.NativeAfterMergingOperands)
        {
            return Result.Success(result.Value.Mesh.WithMetadata(
                result.Value.Mesh.Metadata.WithProperties(m =>
                    m.Set(CoreKeys.CreatedBy, "BooleanOperation (operands merged)"))));
        }

        return Result.Success(result.Value.Mesh);
    }

    public Result<IMesh> Intersect(IMesh meshA, IMesh meshB) =>
        DoBoolean(meshA, meshB, "Intersection", ManifoldKernel.Intersect);

    public Result<IMesh> Subtract(IMesh meshA, IMesh meshB) =>
        DoBoolean(meshA, meshB, "DifferenceAB", ManifoldKernel.Subtract);

    public Result<IMesh> Union(IMesh meshA, IMesh meshB) =>
        DoBoolean(meshA, meshB, "Union", ManifoldKernel.Union);
}
