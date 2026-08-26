using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Geometry.Metadata;

public static class MetadataRefreshExtensions
{
    /// <summary>
    /// Computes fresh topology validation and mesh statistics for <paramref name="mesh"/>,
    /// attaches them to <paramref name="metadata"/>, and returns a new <see cref="IMesh"/> with the updated metadata.
    /// </summary>
    public static IMesh WithRefreshedStatsAndTopology(
        this IMesh mesh,
        IGeometryEngine engine,
        MeshMetadata metadata)
    {
        var topology = engine.Evaluators.ValidateTopology(mesh);
        var stats = engine.Evaluators.GetStatistics(mesh);

        // Stats and topology are cached conveniences, not correctness-critical: a mesh that
        // fails evaluation is still a valid mesh to hand back, just without the cached values.
        var refreshed = metadata.WithProperties(m =>
        {
            if (stats.IsSuccess) m.Set(MeshIOKeys.Stats, stats.Value);
            if (topology.IsSuccess) m.Set(MeshIOKeys.Topology, topology.Value);
        });

        return mesh.WithMetadata(refreshed);
    }
}
