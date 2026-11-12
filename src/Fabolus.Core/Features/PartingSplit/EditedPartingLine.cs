using Fabolus.Core.Geometry;
using System.Text.Json.Serialization;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>
/// A parting line the user settled by hand, kept as the points themselves.
///
/// <para>
/// Everything else in a parting recipe is a parameter, because a parameter can be replayed against
/// whatever the earlier commands produce. A hand-edited line cannot be put that way. It is the outcome
/// of drags, added handles and presses of Smooth made by eye on one particular body, and no setting
/// reproduces them: replaying the edits themselves would mean replaying a shortest-path search per
/// section and trusting it to land where it did, on a line whose whole point is that somebody looked at
/// it and approved it. So the approved line is what is stored.
/// </para>
///
/// <para>
/// The cost is that it no longer follows an upstream change, and that is dealt with where it is used
/// rather than hidden: a stored line is only accepted on a body it still lies on - see
/// <see cref="PartingMeshFeature.GeneratePartingLineFromBody"/>.
/// </para>
/// </summary>
/// <remarks>
/// Coordinates are held flat, x y z per point, rather than as vectors. A save file carries one of these
/// per piece of a split, a line runs to several hundred points, and a vector written as an object
/// repeats its property names for every one of them.
/// </remarks>
public sealed class EditedPartingLine : IEquatable<EditedPartingLine>
{
    /// <summary>Each closed loop as x, y, z, x, y, z ... in order round the loop.</summary>
    public IReadOnlyList<double[]> Loops { get; }

    [JsonConstructor]
    public EditedPartingLine(IReadOnlyList<double[]> loops)
    {
        Loops = loops ?? [];
    }

    /// <summary>The line as it stands, point for point.</summary>
    public static EditedPartingLine From(PartingLine line) =>
        new(line.Loops
            .Select(loop => loop.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray())
            .ToList());

    /// <summary>
    /// The stored points as a line again. Exact: a double written to JSON and read back is the same
    /// double, so the line rebuilt on replay is the one that was approved, not one near it.
    /// </summary>
    public PartingLine ToPartingLine() =>
        new(Loops.Select(loop =>
        {
            var points = new List<Vector3>(loop.Length / 3);
            for (int i = 0; i + 2 < loop.Length; i += 3)
                points.Add(new Vector3(loop[i], loop[i + 1], loop[i + 2]));
            return points;
        }));

    // Compared by content. The recipe this sits in is a record, and two recipes holding the same line
    // have to be equal - a save file read back would otherwise never match the one that wrote it.
    public bool Equals(EditedPartingLine? other)
    {
        if (other is null || other.Loops.Count != Loops.Count) return false;

        for (int i = 0; i < Loops.Count; i++)
            if (!Loops[i].AsSpan().SequenceEqual(other.Loops[i])) return false;

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as EditedPartingLine);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var loop in Loops)
        {
            hash.Add(loop.Length);
            if (loop.Length > 0) hash.Add(loop[0]);
        }
        return hash.ToHashCode();
    }
}
