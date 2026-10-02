using BasicResults;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using System.Numerics;
using EngineRotation = GeometryEngine.Core.Geometry.Primitives.Rotation;

namespace Fabolus.Core.Features.Transforms;

/// <summary>
/// Records the net rotation applied to a mesh - one instance represents the current
/// composed rotation, not a per-action history entry.
/// </summary>
public sealed record RotateCommand(Quaternion Rotation) : IMeshCommand {
    public int Priority => CommandPriority.Transform;

    /// <summary>
    /// Kept as a System.Numerics quaternion because that is the shape saved files hold it in; the
    /// engine takes the same four components. A zero quaternion describes no rotation and leaves
    /// the mesh where it is.
    /// </summary>
    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh) {
        var rotation = EngineRotation.FromQuaternion(Rotation.W, Rotation.X, Rotation.Y, Rotation.Z);
        return rotation.HasValue ? engine.Transforms.Rotate(mesh, rotation.Value) : Result.Success(mesh);
    }

    public string Describe() => "Rotation (auto Z-up)";
}
