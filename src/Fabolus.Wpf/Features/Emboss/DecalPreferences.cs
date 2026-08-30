using Fabolus.Core.Features.Decal;
using Fabolus.Wpf.Features;
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
) : IPreferenceSettings<DecalPreferences>
{
    public static string SectionKey => "decals";

    public static DecalPreferences Default { get; } = new(
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

    public static class Keys
    {
        public const string Enabled = "decals_enabled";
        public const string Scope = "decal_autoplace_scope";
        public const string AutoPlaceFilename = "decal_autoplace_filename";
        public const string FilenameAnchor = "decal_filename_anchor";
        public const string AutoPlaceVolume = "decal_autoplace_volume";
        public const string VolumeAnchor = "decal_volume_anchor";
        public const string Font = "decal_default_font";
        public const string CapHeight = "decal_default_cap_height";
        public const string Depth = "decal_default_depth";
        public const string Operation = "decal_default_operation";
    }

    public static DecalPreferences Read(IPreferenceReader reader) => new(
        reader.GetBool(Keys.Enabled, "Decal tool", Default.Enabled),
        reader.GetEnum(Keys.Scope, "Decal placement scope", Default.Scope),
        reader.GetBool(Keys.AutoPlaceFilename, "Auto-place file name", Default.AutoPlaceFilename),
        reader.GetEnum(Keys.FilenameAnchor, "File name anchor", Default.FilenameAnchor),
        reader.GetBool(Keys.AutoPlaceVolume, "Auto-place volume", Default.AutoPlaceVolume),
        reader.GetEnum(Keys.VolumeAnchor, "Volume anchor", Default.VolumeAnchor),
        reader.GetEnum(Keys.Font, "Decal font", Default.Font),
        reader.GetFloat(Keys.CapHeight, "Decal cap height", Default.CapHeight,
            Ranges.CapHeightMin, Ranges.CapHeightMax),
        reader.GetFloat(Keys.Depth, "Decal depth", Default.Depth, Ranges.DepthMin, Ranges.DepthMax),
        reader.GetEnum(Keys.Operation, "Decal operation", Default.Operation)
    );

    public void Write(IPreferenceWriter writer)
    {
        writer.Set(Keys.Enabled, Enabled);
        writer.SetEnum(Keys.Scope, Scope);
        writer.Set(Keys.AutoPlaceFilename, AutoPlaceFilename);
        writer.SetEnum(Keys.FilenameAnchor, FilenameAnchor);
        writer.Set(Keys.AutoPlaceVolume, AutoPlaceVolume);
        writer.SetEnum(Keys.VolumeAnchor, VolumeAnchor);
        writer.SetEnum(Keys.Font, Font);
        writer.Set(Keys.CapHeight, CapHeight);
        writer.Set(Keys.Depth, Depth);
        writer.SetEnum(Keys.Operation, Operation);
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
