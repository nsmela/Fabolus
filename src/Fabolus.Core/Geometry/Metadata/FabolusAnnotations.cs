using GeometryEngine.Core.Geometry;

namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// What Fabolus knows about a mesh that the engine computed from the geometry itself, carried on
/// the mesh so a caller holding one can read them without going back to the <see cref="Workspace"/>.
/// Both are caches: losing one costs a recomputation, never correctness.
///
/// Only derived facts live here. A mesh's identity, its name and its command history are not
/// properties of the geometry - they belong to the workspace entry the geometry currently fills,
/// and live in <see cref="MeshRecord"/> where no engine operation can drop them.
/// </summary>
public sealed record FabolusAnnotations(
    MeshStatistics? Stats = null,
    TopologyValidation? Topology = null) : IMeshAnnotations {

    /// <summary>
    /// Whether either cached value still describes the mesh <paramref name="operation"/> produced.
    ///
    /// A transform moves vertices without touching connectivity, so the topology audit - counts of
    /// boundary edges, non-manifold edges, duplicate faces - reads exactly the same afterwards and
    /// is worth keeping. <see cref="MeshStatistics"/> is not: its bounds move under any transform
    /// and a scale changes its volume and surface area too. Everything else rebuilds or replaces
    /// the surface, which invalidates both.
    /// </summary>
    public IMeshAnnotations? Carry(MeshOperation operation) => operation switch {
        MeshOperation.Transform when Topology is not null => new FabolusAnnotations(Topology: Topology),
        _ => null,
    };
}

/// <summary>
/// Reading and writing <see cref="FabolusAnnotations"/> through the engine's single annotation slot.
/// </summary>
public static class FabolusAnnotationExtensions {
    /// <summary>
    /// This mesh's annotations, or an empty set when it carries none - a mesh straight out of a
    /// boolean or an import has nothing cached yet, which is a normal state rather than an error.
    /// </summary>
    public static FabolusAnnotations Annotations(this IMesh mesh) =>
        mesh.Metadata.Annotations as FabolusAnnotations ?? new FabolusAnnotations();

    public static MeshStatistics? Stats(this IMesh mesh) => mesh.Annotations().Stats;

    public static TopologyValidation? Topology(this IMesh mesh) => mesh.Annotations().Topology;

    public static IMesh WithAnnotations(this IMesh mesh, FabolusAnnotations annotations) =>
        mesh.WithMetadata(mesh.Metadata.WithAnnotations(annotations));

    /// <summary>
    /// Measures the mesh and caches both results on it. A measurement that fails leaves that value
    /// absent rather than failing the call: these are caches, and a mesh that cannot be measured is
    /// still a mesh worth handing back.
    /// </summary>
    public static IMesh WithMeasurements(this IMesh mesh, IGeometryEngine engine) {
        var stats = engine.Evaluators.GetStatistics(mesh);
        var topology = engine.Evaluators.ValidateTopology(mesh);

        return mesh.WithAnnotations(new FabolusAnnotations(
            stats.IsSuccess ? stats.Value : null,
            topology.IsSuccess ? topology.Value : null));
    }

    /// <summary>
    /// Re-measures the bounds and volume only, keeping the topology audit already cached. For a
    /// transform, which moves a mesh without re-meshing it - re-validating topology there would
    /// walk every edge to learn what the mesh already knew.
    /// </summary>
    public static IMesh WithRefreshedStats(this IMesh mesh, IGeometryEngine engine) {
        var stats = engine.Evaluators.GetStatistics(mesh);

        return mesh.WithAnnotations(mesh.Annotations() with {
            Stats = stats.IsSuccess ? stats.Value : null,
        });
    }
}
