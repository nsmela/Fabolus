using Fabolus.Core.AirChannel;
using HelixToolkit.Wpf.SharpDX;

namespace Fabolus.Wpf.Features.Channels;

public interface IAirChannel {
    public Guid GUID { get; init; }
    public float Height { get; set; }
    public ChannelTypes ChannelType { get; }
    public abstract IAirChannel New(); 
    public IAirChannel WithHit(HitTestResult hit, bool isPreview = false);
    public MeshGeometry3D Geometry { get; set; }
}
