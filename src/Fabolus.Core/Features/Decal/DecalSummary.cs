using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Decal;

/// <summary>
/// Shared wording for the decal summary in <see cref="IMeshCommand.Describe"/>, so the base
/// and mould decal commands cannot drift apart in how they read.
/// </summary>
internal static class DecalSummary
{
    public static string Of(IReadOnlyList<TextDecal> decals) => decals.Count switch
    {
        0 => "none",
        1 => $"\"{decals[0].Text}\"",
        _ => $"{decals.Count} decals"
    };
}
