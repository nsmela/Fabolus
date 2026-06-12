namespace Fabolus.Core.Geometry;

/// <summary>
/// Result of mesh topology validation.
/// </summary>
public sealed class TopologyValidation
{
    public bool IsValid => !HasCorruptTopology 
        && IsWatertight 
        && !HasOrphanedVertices 
        && !HasDegenerateTriangles 
        && IsManifold 
        && SelfIntersectionCount == 0;
    
    public bool HasCorruptTopology { get; init; }
    public bool IsWatertight { get; init; }
    public bool IsManifold { get; init; }
    public bool HasOrphanedVertices { get; init; }
    public bool HasDegenerateTriangles { get; init; }
    
    public int VertexCount { get; init; }
    public int TriangleCount { get; init; }
    public int BoundaryEdgeCount { get; init; }
    public int NonManifoldEdgeCount { get; init; }
    public int SelfIntersectionCount { get; init; }
}
