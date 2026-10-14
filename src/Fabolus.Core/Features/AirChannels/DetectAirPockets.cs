using System.Collections.Immutable;
using BasicResults;
using Fabolus.Core.Geometry;
using GeometryEngine.Core.Geometry;
using GeometryEngine.Core.Geometry.Primitives;

namespace Fabolus.Core.Features.AirChannels;

/// <summary>
/// How deep a pocket has to be before it is worth venting, and how close two vents may stand.
/// </summary>
/// <param name="MinimumDepth">
/// How far below its peak the air in a pocket has to pool before it could spill over towards
/// somewhere higher. Shallower pockets are mesh noise, or trap too little air to matter.
/// </param>
/// <param name="MinimumSpacing">
/// The closest two channels may meet the surface. Two closer than this would overlap or leave a
/// wall between them too thin to print; only the higher one is kept. Measured in 3D rather than
/// in plan, so a channel low on a wall does not crowd out one on the top above it.
/// </param>
public sealed record AirPocketSettings(double MinimumDepth, double MinimumSpacing);

/// <summary>
/// Where a channel should be placed to vent one pocket: a point on the surface at the top of the
/// pocket, the surface normal there, and how much air the pocket would otherwise hold back.
/// </summary>
/// <param name="Depth">How far below the peak the pocket spills; infinite for the highest pocket, which never spills.</param>
public readonly record struct AirPocket(Vector3 Point, Vector3 Normal, double Depth);

/// <summary>
/// Finds the places a casting would trap air: the high points of the bolus surface, which are the
/// ceiling of the mould cavity once it is poured. Silicone rises through the cavity and pushes air
/// up ahead of it, so every local peak ends up holding a bubble unless a channel vents it.
/// </summary>
/// <remarks>
/// <para>A pocket's depth is its peak's topographic prominence, which the engine finds
/// (<see cref="IGeometryEvaluators.FindPeaks"/>): how far the surface drops below the peak before
/// it joins somewhere higher, which is how far the trapped air reaches down before it can spill.</para>
///
/// <para>A pocket already vented by an existing channel is skipped. A channel only vents the part
/// of a pocket above where it enters - once the silicone rises past its tip, whatever air is left
/// higher up is sealed in - so it counts only if it stands within the minimum depth of the peak.</para>
/// </remarks>
public sealed class DetectAirPockets(IGeometryEngine Engine)
{
    /// <summary>
    /// The pockets that still need a channel. A one-off shortcut for <see cref="Analyze"/>
    /// followed by <see cref="AirPocketMap.Unvented"/>.
    /// </summary>
    /// <param name="existingVents">
    /// Points where channels already meet the surface. Pockets they vent are skipped, and no new
    /// channel is placed within the minimum spacing of one.
    /// </param>
    public Result<IReadOnlyList<AirPocket>> Execute(IMesh mesh, AirPocketSettings settings, IReadOnlyList<Vector3> existingVents)
    {
        var map = Analyze(mesh);
        if (map.IsFailure)
            return map.Error;

        return map.Value.Unvented(settings, existingVents);
    }

    /// <summary>
    /// Finds every peak of the mesh and how deep its pocket is. This is the part that walks the
    /// whole mesh; the map it returns answers <see cref="AirPocketMap.Unvented"/> cheaply, so a
    /// caller asking again whenever the channels change should keep the map rather than call
    /// <see cref="Execute"/>.
    /// </summary>
    public Result<AirPocketMap> Analyze(IMesh mesh)
    {
        if (mesh.IsEmpty)
            return Result<AirPocketMap>.Success(AirPocketMap.Empty);

        var peaks = Engine.Evaluators.FindPeaks(mesh, Direction.Z);
        if (peaks.IsFailure)
            return peaks.Error;

        return Result<AirPocketMap>.Success(new AirPocketMap(peaks.Value));
    }
}

/// <summary>
/// The peaks of one mesh and how deep each pocket is, from <see cref="DetectAirPockets.Analyze"/>.
/// Asking which of them still need a channel is cheap, so keep one of these per mesh and ask
/// again whenever the channels or the settings change.
/// </summary>
/// <remarks>Not safe to query from several threads at once: it caches each summit as it finds it.</remarks>
public sealed class AirPocketMap
{
    // How far below a peak the surface still counts as its summit. A flat top is summit all over,
    // and the channel goes in the middle of it rather than at whichever corner sorted first.
    private const double SummitTolerance = 0.05;

    /// <summary>A map with no pockets in it, as of an empty mesh.</summary>
    public static readonly AirPocketMap Empty = new(null);

    private readonly ISurfacePeaks? _peaks;

    // Every peak facing up, highest first. A local peak facing down is the top of something
    // poking up into the cavity from below - the silicone flows over it, and air has nowhere to
    // collect.
    private readonly IReadOnlyList<SurfacePeak> _upward;

    // Where the channel for each peak goes; depends only on the mesh, so worked out once.
    private readonly Dictionary<int, (Vector3 Point, Vector3 Normal)> _summits = [];

    internal AirPocketMap(ISurfacePeaks? peaks)
    {
        _peaks = peaks;
        _upward = peaks?.Peaks.Where(peak => peak.Normal.Z > 0).ToList() ?? [];
    }

    /// <summary>
    /// The pockets at least <see cref="AirPocketSettings.MinimumDepth"/> deep that no existing
    /// channel vents, thinned out to <see cref="AirPocketSettings.MinimumSpacing"/>, highest first.
    /// </summary>
    /// <param name="existingVents">
    /// Points where channels already meet the surface. Pockets they vent are skipped, and no new
    /// channel is placed within the minimum spacing of one.
    /// </param>
    public Result<IReadOnlyList<AirPocket>> Unvented(AirPocketSettings settings, IReadOnlyList<Vector3> existingVents)
    {
        if (settings is null)
            return new Error("AirPockets.NullSettings", "Air pocket settings must be provided.");

        if (settings.MinimumDepth <= 0)
            return new Error("AirPockets.InvalidDepth", "The minimum pocket depth must be greater than zero.");

        if (_peaks is null)
            return Result<IReadOnlyList<AirPocket>>.Success(Array.Empty<AirPocket>());

        ImmutableArray<Vector3> vents = [.. existingVents];
        var placed = new List<AirPocket>();

        foreach (var peak in _upward)
        {
            if (peak.Prominence < settings.MinimumDepth)
                continue;

            // Both tests are needed: the vent has to stand high enough, and on this pocket rather
            // than on some other surface at that height. Its nearest vertex can sit higher than it
            // on a coarse mesh, so the height comes from the vent itself.
            var floor = peak.Point.Z - settings.MinimumDepth;
            var onCap = _peaks.WithinReach(peak, settings.MinimumDepth, vents);
            var vented = vents.Where((vent, i) => vent.Z >= floor && onCap[i]).Any();
            if (vented)
                continue;

            var (point, normal) = Summit(peak);
            var pocket = new AirPocket(point, normal, peak.Prominence);

            var crowded = vents.Concat(placed.Select(p => p.Point))
                .Any(other => other.DistanceTo(pocket.Point) < settings.MinimumSpacing);
            if (crowded)
                continue;

            placed.Add(pocket);
        }

        return Result<IReadOnlyList<AirPocket>>.Success(placed);
    }

    private (Vector3 Point, Vector3 Normal) Summit(SurfacePeak peak)
    {
        if (_summits.TryGetValue(peak.Id, out var cached))
            return cached;

        var summit = _peaks!.Summit(peak, SummitTolerance);
        _summits[peak.Id] = summit;
        return summit;
    }
}
