using CommunityToolkit.Mvvm.Messaging.Messages;
using Fabolus.Core.Meshes;

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
public sealed class SplitRequestModelsMessage : RequestMessage<MeshModel[]>;
public sealed class SplitRequestViewOptionsMessage : RequestMessage<SplitViewOptions>;
