namespace Fabolus.Core.Features.Overhangs;

/// <summary>
/// Inputs for overhang colouring: the direction overhangs face toward, the gradient to
/// paint with, and the angle band (degrees) that maps onto the gradient's [0, 1] range.
/// A vertex whose normal faces the direction head-on sits at <paramref name="MinAngleDegrees"/>
/// (gradient start); one facing away sits at or beyond <paramref name="MaxAngleDegrees"/>
/// (gradient end).
/// </summary>
public sealed record OverhangSettings(
    OverhangDirection Direction,
    ColourGradient Gradient,
    float MinAngleDegrees = 0f,
    float MaxAngleDegrees = 90f);