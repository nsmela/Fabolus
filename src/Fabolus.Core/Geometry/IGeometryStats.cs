
namespace Fabolus.Core.Geometry;

public interface IGeometryStats
{
    int VertexCount { get; }
    int TriangleCount { get; }
    double SurfaceArea { get; }
    double Volume { get; }
}
