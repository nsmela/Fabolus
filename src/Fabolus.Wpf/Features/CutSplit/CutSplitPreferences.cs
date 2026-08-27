using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Features.CutSplit;

/// <summary>
/// User preferences for Cut and Split views and availability.
/// </summary>
public sealed record CutSplitPreferences(
    bool CutViewEnabled,
    CutViewScope CutScope,
    bool SplitViewEnabled
) : IPreferenceSettings
{
    public static readonly CutSplitPreferences Default = new(
        CutViewEnabled: false,
        CutScope: CutViewScope.Base,
        SplitViewEnabled: false
    );

    public CutSplitPreferences Clamped() => new(
        CutViewEnabled,
        Enum.IsDefined(CutScope) ? CutScope : Default.CutScope,
        SplitViewEnabled
    );
}
