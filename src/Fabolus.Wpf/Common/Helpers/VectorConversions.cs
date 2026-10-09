namespace Fabolus.Wpf.Common.Helpers;

/// <summary>
/// Between the engine's double-precision vectors, which everything in Fabolus.Core speaks, and the
/// single-precision ones Helix draws with and reports hits in. Rendering narrows to float on the way
/// out and widens back on the way in; nothing here rounds anything that is not about to be drawn.
/// </summary>
internal static class VectorConversions
{
    public static SharpDX.Vector3 ToSharpDX(this Vector3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public static System.Numerics.Vector3 ToNumerics(this Vector3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public static Vector3 ToVec3(this SharpDX.Vector3 v) => new(v.X, v.Y, v.Z);

    public static Vector3 ToVec3(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
