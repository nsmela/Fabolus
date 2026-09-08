using SharpDX;

namespace Fabolus.Wpf.Common.Extensions;

internal static class SharpDxExtensions {

    public static System.Numerics.Vector3 ToNumericsVector3(this Vector3 vector) => new System.Numerics.Vector3(vector.X, vector.Y, vector.Z);
}
