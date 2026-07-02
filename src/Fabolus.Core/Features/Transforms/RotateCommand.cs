using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using System.Numerics;

namespace Fabolus.Core.Features.Transforms;

/// <summary>
/// Records the net rotation applied to a mesh - one instance represents the current
/// composed rotation, not a per-action history entry.
/// </summary>
public sealed record RotateCommand(Quaternion Rotation) : IMeshCommand {
    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh) => engine.Transforms.Rotate(mesh, Rotation);
}
