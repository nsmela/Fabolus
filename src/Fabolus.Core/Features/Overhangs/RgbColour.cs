
namespace Fabolus.Core.Features.Overhangs;

/// <summary>An RGB colour with channels in the [0, 1] range.</summary>
public readonly record struct RgbColour(float R, float G, float B) {
    /// <summary>Linearly interpolates between two colours; <paramref name="t"/> is clamped to [0, 1].</summary>
    public static RgbColour Lerp(RgbColour a, RgbColour b, float t) {
        t = Math.Clamp(t, 0f, 1f);
        return new RgbColour(
            a.R + (b.R - a.R) * t,
            a.G + (b.G - a.G) * t,
            a.B + (b.B - a.B) * t);
    }
}