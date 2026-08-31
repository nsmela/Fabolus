using Fabolus.Core.Common;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.PartingSplit;

public static class MouldMeshErrors
{
    public static readonly Error NoMouldMetadata = new("MouldMesh.NoMouldMetadata", "The mesh lacks metadata for a complete mould");
}

public class MouldMesh
{
    public IMesh Mesh { get; }

    private MouldMesh(IMesh mesh)
    {
        Mesh = mesh;
    }

    // TODO: summary
    public static Result<MouldMesh> Create(IMesh mesh)
    {
        var result = IsMould(mesh);
        if (result.IsFailure)
            return result.Error;

        return new MouldMesh(mesh);
    }

    public static Result IsMould(IMesh mesh)
    {
        if (mesh is null)
            return MeshErrors.NullSource;

        var metadataResult = mesh.Metadata.MouldDefinition();

        if (metadataResult.HasNoValue)
            return MouldMeshErrors.NoMouldMetadata;

        return Result.Success();
    }
}
