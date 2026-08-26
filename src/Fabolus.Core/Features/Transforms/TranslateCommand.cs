using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using System.Numerics;

namespace Fabolus.Core.Features.Transforms;

/// <summary>
/// Records the net translation applied to a mesh - one instance represents the current
/// composed translation, not a per-action history entry.
/// </summary>
public sealed record TranslateCommand(Vector3 Translation) : IMeshCommand {
    public int Priority => CommandPriority.Transform;

    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh) => engine.Transforms.Translate(mesh, Translation.X, Translation.Y, Translation.Z);

    /// <summary>
    /// Not shown: translation is automatic placement, not an operation the user asked for.
    /// </summary>
    public string Describe() => string.Empty;
}
