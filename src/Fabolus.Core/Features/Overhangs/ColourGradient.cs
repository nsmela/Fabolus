using Fabolus.Core.Common;

namespace Fabolus.Core.Features.Overhangs;

/// <summary>A colour positioned within a gradient. <see cref="Position"/> lives in [0, 1].</summary>
public readonly record struct ColourStop(float Position, RgbColour Color);

/// <summary>
/// An ordered set of colour stops sampled continuously across [0, 1]. Built through
/// <see cref="Create"/>, so a gradient always holds at least two stops whose positions
/// sit inside [0, 1]; sampling between them is a straight linear interpolation.
/// </summary>
public sealed class ColourGradient {
    private readonly ColourStop[] _stops; // ascending by Position

    private ColourGradient(ColourStop[] sortedStops) => _stops = sortedStops;

    public IReadOnlyList<ColourStop> Stops => _stops;

    /// <summary>
    /// The default overhang gradient: red where a surface faces the overhang direction
    /// (t = 0), through yellow, to green where it faces away (t = 1). Pass your own stops
    /// to <see cref="Create"/> for any other palette.
    /// </summary>
    public static ColourGradient Overhang { get; } = Create(
        new ColourStop(0f, new RgbColour(0.85f, 0.15f, 0.15f)),  // facing the overhang dir -> red
        new ColourStop(0.5f, new RgbColour(0.95f, 0.85f, 0.20f)),  // midway                  -> yellow
        new ColourStop(1f, new RgbColour(0.30f, 0.70f, 0.30f))   // facing away             -> green
    ).Value;

    public static Result<ColourGradient> Create(params ColourStop[] stops) {
        if (stops is null || stops.Length < 2)
            return new Error("Overhang.GradientTooFewStops", "A gradient needs at least two colour stops.");

        if (stops.Any(s => s.Position < 0f || s.Position > 1f))
            return new Error("Overhang.GradientStopOutOfRange", "Every gradient stop must sit within [0, 1].");

        return new ColourGradient(stops.OrderBy(s => s.Position).ToArray());
    }

    /// <summary>Samples the gradient at <paramref name="t"/>, clamped to [0, 1].</summary>
    public RgbColour Sample(float t) {
        t = Math.Clamp(t, 0f, 1f);

        if (t <= _stops[0].Position) return _stops[0].Color;
        if (t >= _stops[^1].Position) return _stops[^1].Color;

        for (int i = 1; i < _stops.Length; i++) {
            var hi = _stops[i];
            if (t > hi.Position) continue;

            var lo = _stops[i - 1];
            var span = hi.Position - lo.Position;
            var local = span <= 0f ? 0f : (t - lo.Position) / span;
            return RgbColour.Lerp(lo.Color, hi.Color, local);
        }

        return _stops[^1].Color; // unreachable: t is bracketed above
    }
}