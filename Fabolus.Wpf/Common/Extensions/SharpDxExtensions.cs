using SharpDX;

namespace Fabolus.Wpf.Common.Extensions;

internal static class SharpDxExtensions {

    // calculates the distance between two points
    public static int DistanceTo(this Point point, Point target) {
        var dx = point.X - target.X;
        var dy = point.Y - target.Y;
        return (int)Math.Sqrt(dx * dx + dy * dy);
    }

    // calculates the squared distance between two points
    // often used as a faster calculation for distances
    public static int DistanceSquaredTo(this Point point, Point target) {
        var dx = point.X - target.X;
        var dy = point.Y - target.Y;
        return (int)(dx * dx + dy * dy);
    }

    public static System.Numerics.Vector3 ToNumericsVector3(this Vector3 vector) => new System.Numerics.Vector3(vector.X, vector.Y, vector.Z);
}
