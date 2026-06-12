using Fabolus.Core.Common;

namespace Fabolus.Core.Geometry;

public enum RotationAxis
{
    X,
    Y,
    Z
}

/// <summary>
/// Interface for geometric transformation operations.
/// </summary>
public interface IGeometryTransforms
{
    /// <summary>
    /// Translates a mesh by the given delta.
    /// </summary>
    Result<IMesh> Translate(IMesh source, double dx, double dy, double dz);
    
    /// <summary>
    /// Scales a mesh by the given factor.
    /// </summary>
    Result<IMesh> Scale(IMesh source, double factor);
    
    /// <summary>
    /// Rotates a mesh around an axis.
    /// </summary>
    Result<IMesh> Rotate(IMesh source, double angleRadians, double axisX, double axisY, double axisZ);

    Result<IMesh> ClearRotation(IMesh source);
}
