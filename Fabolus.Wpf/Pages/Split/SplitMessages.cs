using CommunityToolkit.Mvvm.Messaging.Messages;
using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.PartingTools;

namespace Fabolus.Wpf.Pages.Split;

public record struct SplitViewOptions(
    bool ShowBolus,
    bool ShowNegativeParting,
    bool ShowPositiveParting,
    bool ShowPullRegions,
    bool ShowPartingLine,
    bool ShowPartingMesh,
    bool ExplodePartingMeshes);

public sealed record SplitSeperationDistanceMessage(float Distance);
public sealed record UpdateSplitViewOptionsMessage(SplitViewOptions Options);
public sealed record SplitSettingsMessage(CuttingMeshParams Settings);
public sealed record SplitResultsMessage(CuttingMeshResults Results);
public sealed class SplitRequestSettingsMessage : RequestMessage<CuttingMeshParams>;
public sealed class SplitRequestViewOptionsMessage : RequestMessage<SplitViewOptions>;
public sealed class SplitRequestResultsMessage : RequestMessage<CuttingMeshResults>;
