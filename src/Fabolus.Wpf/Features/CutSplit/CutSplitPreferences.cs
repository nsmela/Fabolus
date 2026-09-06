using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Features.CutSplit;

/// <summary>
/// User preferences for the Cut view and its availability.
/// </summary>
public sealed record CutSplitPreferences(
    bool CutViewEnabled,
    CutViewScope CutScope
) : IPreferenceSettings<CutSplitPreferences>
{
    public static CutSplitPreferences Default { get; } = new(
        CutViewEnabled: false,
        CutScope: CutViewScope.Base
    );

    public static class Keys
    {
        public const string CutViewEnabled = "cut_view_enabled";
        public const string CutScope = "cut_view_scope";
    }

    public static CutSplitPreferences Read(IPreferenceReader reader) => new(
        reader.GetBool(Keys.CutViewEnabled, "Cut view", Default.CutViewEnabled),
        reader.GetEnum(Keys.CutScope, "Cut view scope", Default.CutScope)
    );

    public void Write(IPreferenceWriter writer)
    {
        writer.Set(Keys.CutViewEnabled, CutViewEnabled);
        writer.SetEnum(Keys.CutScope, CutScope);
    }

    public CutSplitPreferences Clamped() => new(
        CutViewEnabled,
        Enum.IsDefined(CutScope) ? CutScope : Default.CutScope
    );
}
