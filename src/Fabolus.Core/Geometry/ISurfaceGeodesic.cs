using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// The shortest path across a surface between two points on it, over and over, against one mesh.
///
/// <para>
/// A type rather than a bare function for the same reason <see cref="ISurfaceProjector"/> is: the cost
/// is in the setup. Answering one query needs a spatial index to find the two ends on the surface and a
/// topology to walk between them, and building those per call would dwarf every caller here - a handle
/// dragged across the rim asks for two paths per frame against a mesh that does not change. Holding the
/// geodesic holds both, and disposing it releases the native mesh behind them.
/// </para>
///
/// <para>
/// Unconstrained, and that is the whole of what separates it from
/// <see cref="PartingBandGraph.WalkGeodesic"/>. This is the shortest path on the mesh; that one is the
/// shortest path inside a corridor of band faces chosen to run the right way round the rim. The two
/// answer different questions and neither is a better version of the other - see
/// <see cref="PartingLineEditor.Move"/> for which one a parting line wants and why it is a choice.
/// </para>
/// </summary>
public interface ISurfaceGeodesic : IDisposable
{
    /// <summary>
    /// The path from <paramref name="from"/> to <paramref name="to"/> across the surface, as the points
    /// where it crosses mesh edges - ends included. Null when the two ends cannot be joined on the
    /// surface at all, which is what a mesh in more than one piece produces.
    /// </summary>
    /// <remarks>
    /// Both ends are projected onto the surface first, so a point held slightly off it - which is every
    /// point a user's cursor produces - is taken as the nearest place on it they could have meant.
    /// </remarks>
    IReadOnlyList<Vector3>? Path(Vector3 from, Vector3 to);
}
