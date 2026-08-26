using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Represents the result of a ray-mesh intersection query.
/// </summary>
public readonly record struct RaycastHit(
    Vector3 Point,
    Vector3 Normal,
    float Distance);
