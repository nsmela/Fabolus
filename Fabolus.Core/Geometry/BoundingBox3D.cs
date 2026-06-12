using System.Numerics;

namespace Fabolus.Core.Geometry;

public readonly record struct BoundingBox3D(Vector3 Min, Vector3 Max);
