using Fabolus.Core.Common;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Decal;

public static class TextEmbossMetadataExtensions
{
    /// <summary>
    /// Extracts all applied text decals from the mesh's command history (<see cref="DecalCommand"/> and <see cref="MouldDecalCommand"/>).
    /// </summary>
    public static Maybe<IReadOnlyList<TextDecal>> TextDecals(this MeshMetadata metadata)
    {
        var list = new List<TextDecal>();

        foreach (var cmd in metadata.Commands)
        {
            if (cmd is DecalCommand decalCmd)
            {
                list.AddRange(decalCmd.Decals);
            }
            else if (cmd is MouldDecalCommand mouldDecalCmd)
            {
                list.AddRange(mouldDecalCmd.Decals);
            }
        }

        return list.Count > 0 ? Maybe<IReadOnlyList<TextDecal>>.Some(list) : Maybe<IReadOnlyList<TextDecal>>.None();
    }
}
