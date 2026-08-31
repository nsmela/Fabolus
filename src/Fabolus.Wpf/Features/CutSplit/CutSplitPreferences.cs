using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Features.CutSplit;

/// <summary>
/// User preferences for Cut and Split views and availability.
/// </summary>
public sealed record CutSplitPreferences(
    bool CutViewEnabled,
    CutViewScope CutScope,
    bool SplitViewEnabled
) : IPreferenceSettings<CutSplitPreferences>
{
    public static CutSplitPreferences Default { get; } = new(
        CutViewEnabled: false,
        CutScope: CutViewScope.Base,
        SplitViewEnabled: false
    );

    public static class Keys
    {
        public const string CutViewEnabled = "cut_view_enabled";
        public const string CutScope = "cut_view_scope";
        public const string SplitViewEnabled = "split_view_enabled";
    }

    public static CutSplitPreferences Read(IPreferenceReader reader) => new(
        reader.GetBool(Keys.CutViewEnabled, "Cut view", Default.CutViewEnabled),
        reader.GetEnum(Keys.CutScope, "Cut view scope", Default.CutScope),
        reader.GetBool(Keys.SplitViewEnabled, "Split view", Default.SplitViewEnabled)
    );

    public void Write(IPreferenceWriter writer)
    {
        writer.Set(Keys.CutViewEnabled, CutViewEnabled);
        writer.SetEnum(Keys.CutScope, CutScope);
        writer.Set(Keys.SplitViewEnabled, SplitViewEnabled);
    }

    public CutSplitPreferences Clamped() => new(
        CutViewEnabled,
        Enum.IsDefined(CutScope) ? CutScope : Default.CutScope,
        SplitViewEnabled
    );
}
