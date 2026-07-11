using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// The 3D loop(s) that mark where a mould should be divided along a given pull direction.
/// A mould with an internal hole (e.g. a tunnel/channel) produces one loop for the outer
/// silhouette plus one additional loop per hole - each of those extra loops is a signal that
/// an additional parting (shut-off) surface is required to separate the mould cleanly there.
/// </summary>
public sealed record PartingLine
{
    /// <summary>
    /// Each entry is one closed loop of ordered 3D points. The loop with the largest projected
    /// area (relative to the pull direction) is the outer silhouette; every other loop marks an
    /// internal hole.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Vector3>> Loops { get; }

    public PartingLine(IEnumerable<IEnumerable<Vector3>> loops)
    {
        Loops = loops.Select(l => (IReadOnlyList<Vector3>)l.ToList()).ToList();
    }

    public static PartingLine Empty { get; } = new(Array.Empty<Vector3[]>());

    public bool IsValid => Loops.Count > 0 && Loops.All(l => l.Count > 2);

    /// <summary>
    /// Number of loops beyond the outer silhouette - i.e. how many internal holes were
    /// detected and will need their own shut-off parting surface.
    /// </summary>
    public int InternalHoleCount => Math.Max(0, Loops.Count - 1);
}
