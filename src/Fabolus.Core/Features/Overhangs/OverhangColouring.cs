
namespace Fabolus.Core.Features.Overhangs;

/// <summary>
/// Per-vertex overhang colouring for a mesh. <see cref="Colors"/> is interleaved RGB
/// (r, g, b in 0-1), one triple per render vertex and in the same order as the engine's
/// render data, so it can be handed straight to the viewport. <see cref="AnglesDegrees"/>
/// is the raw per-vertex overhang angle, kept for inspection and stats.
/// </summary>
public sealed class OverhangColoring {
    public double[] Colors { get; }
    public IReadOnlyList<float> AnglesDegrees { get; }

    public OverhangColoring(double[] colors, IReadOnlyList<float> anglesDegrees) {
        Colors = colors;
        AnglesDegrees = anglesDegrees;
    }

    public int VertexCount => AnglesDegrees.Count;
}