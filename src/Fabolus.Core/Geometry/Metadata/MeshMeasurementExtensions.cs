namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// Reading a mesh's measurements. The engine remembers what it measured on the mesh itself -
/// which cannot change - and hands those answers on through anything that keeps the geometry or
/// only moves it rigidly, so after the first call these are lookups. Nothing about them is kept
/// on the Fabolus side.
///
/// Only derived facts come from here. A mesh's identity, its name and its command history are not
/// properties of the geometry - they belong to the workspace entry the geometry currently fills,
/// and live in <see cref="MeshRecord"/> where no engine operation can drop them.
/// </summary>
public static class MeshMeasurementExtensions {
    /// <summary>The mesh's statistics, or null when it cannot be measured - an empty mesh.</summary>
    public static MeshStatistics? Stats(this IMesh mesh, IGeometryEngine engine) {
        var stats = engine.Evaluators.GetStatistics(mesh);
        return stats.IsSuccess ? stats.Value : null;
    }

    /// <summary>The mesh's topology audit, or null when it cannot be taken - an empty mesh.</summary>
    public static TopologyValidation? Topology(this IMesh mesh, IGeometryEngine engine) {
        var topology = engine.Evaluators.ValidateTopology(mesh);
        return topology.IsSuccess ? topology.Value : null;
    }

    /// <summary>
    /// Takes both measurements now and hands back the same mesh. Features call this on the
    /// background thread that produced the geometry, so the panels reading it afterwards on the UI
    /// thread find the answers already there instead of walking the mesh themselves.
    /// </summary>
    public static IMesh Measured(this IMesh mesh, IGeometryEngine engine) {
        _ = engine.Evaluators.GetStatistics(mesh);
        _ = engine.Evaluators.ValidateTopology(mesh);
        return mesh;
    }
}
