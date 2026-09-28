using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Decal;

public static class TextEmbossRecordExtensions {
    /// <summary>
    /// Every text decal applied to this entry, gathered from its command history
    /// (<see cref="DecalCommand"/> and <see cref="MouldDecalCommand"/>). Empty when none have been.
    /// </summary>
    public static IReadOnlyList<TextDecal> TextDecals(this MeshRecord record) {
        var decals = new List<TextDecal>();

        foreach (var command in record.Commands) {
            switch (command) {
                case DecalCommand decal:
                    decals.AddRange(decal.Decals);
                    break;
                case MouldDecalCommand mouldDecal:
                    decals.AddRange(mouldDecal.Decals);
                    break;
            }
        }

        return decals;
    }
}
