namespace Fabolus.Core.Geometry;

/// <summary>
/// Statistical information about a mesh.
/// </summary>
public sealed class MeshStatistics
{
    public int VertexCount { get; init; }
    public int TriangleCount { get; init; }
    public int EdgeCount { get; init; }
    public int BoundaryEdgeCount { get; init; }
    
    public double Volume { get; init; }
    public double SurfaceArea { get; init; }
    
    public double MinX { get; init; }
    public double MinY { get; init; }
    public double MinZ { get; init; }
    public double MaxX { get; init; }
    public double MaxY { get; init; }
    public double MaxZ { get; init; }
    
    public double BoundingBoxVolume => 
        (MaxX - MinX) * (MaxY - MinY) * (MaxZ - MinZ);
}