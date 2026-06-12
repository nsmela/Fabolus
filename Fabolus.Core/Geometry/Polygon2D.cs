using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fabolus.Core.Geometry;

public sealed record Polygon2D
{
    public required IReadOnlyList<Vector2> OuterBoundary { get; init; }
    public IReadOnlyList<IReadOnlyList<Vector2>> Holes { get; init; } = [];
}
