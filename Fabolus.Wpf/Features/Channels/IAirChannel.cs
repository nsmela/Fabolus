using Fabolus.Core.AirChannel;
using HelixToolkit.Wpf.SharpDX;

namespace Fabolus.Wpf.Features.Channels;

public interface IAirChannel {
    Guid GUID { get; init; }
    ChannelTypes ChannelType { get; }
    static abstract AirChannel New();
    AirChannel WithHit(HitTestResult hit, bool isPreview = false);
    MeshGeometry3D Geometry { get; set; }
}
