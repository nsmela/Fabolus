using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Emboss;

/// <summary>
/// Legacy alias for <see cref="ClearDecals"/>.
/// </summary>
public sealed class ClearTextEmboss
{
    private readonly ClearDecals _clearDecals;

    public ClearTextEmboss(IGeometryEngine engine)
    {
        _clearDecals = new ClearDecals(engine);
    }

    public Result<IMesh> Clear(IMesh mesh) => _clearDecals.Clear(mesh);

    public Result<Workspace> Execute(Workspace workspace) => _clearDecals.Execute(workspace);
}
