using Fabolus.Core.Features.Decal;
using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Features.Decal;

/// <summary>
/// User preferences for decals and auto-placement.
/// </summary>
public sealed record DecalPreferences(
    bool Enabled,
    DecalAutoPlaceScope Scope,
    bool AutoPlaceFilename,
    DecalAnchor FilenameAnchor,
    bool AutoPlaceVolume,
    DecalAnchor VolumeAnchor,
    DecalFont Font,
    float CapHeight,
    float Depth,
    EmbossOperation Operation
) : IPreferenceSettings
{
    public static readonly DecalPreferences Default = new(
        Enabled: true,
        Scope: DecalAutoPlaceScope.Mould,
        AutoPlaceFilename: true,
        FilenameAnchor: DecalAnchor.Front,
        AutoPlaceVolume: true,
        VolumeAnchor: DecalAnchor.Back,
        Font: DecalFont.Sans,
        CapHeight: TextDecal.DefaultCapHeight,
        Depth: TextDecal.DefaultDepth,
        Operation: TextDecal.DefaultOperation
    );

    public static class Ranges
    {
        public const float CapHeightMin = 2.0f;
        public const float CapHeightMax = 20.0f;
        public const float DepthMin = 0.2f;
        public const float DepthMax = 5.0f;
    }

    public DecalPreferences Clamped() => new(
        Enabled,
        Enum.IsDefined(Scope) ? Scope : Default.Scope,
        AutoPlaceFilename,
        Enum.IsDefined(FilenameAnchor) ? FilenameAnchor : Default.FilenameAnchor,
        AutoPlaceVolume,
        Enum.IsDefined(VolumeAnchor) ? VolumeAnchor : Default.VolumeAnchor,
        Enum.IsDefined(Font) ? Font : Default.Font,
        Math.Clamp(CapHeight, Ranges.CapHeightMin, Ranges.CapHeightMax),
        Math.Clamp(Depth, Ranges.DepthMin, Ranges.DepthMax),
        Enum.IsDefined(Operation) ? Operation : Default.Operation
    );
}
