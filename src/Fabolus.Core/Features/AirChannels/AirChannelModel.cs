using System.Numerics;

namespace Fabolus.Core.Features.AirChannels;

public record AirChannelModel(
    Guid Id,
    AirChannelType Type,
    double TipDiameter,
    double ChannelDiameter,
    double TipLength,
    IAirChannel DomainModel)
{
    public Vector3 Position => DomainModel switch
    {
        StraightAirChannel s => s.StartPoint,
        AngledAirChannel a => a.StartPoint,
        PaintedAirChannel p => p.Path.Count > 0 ? p.Path[0] : Vector3.Zero,
        _ => Vector3.Zero
    };

    public Vector3 Direction => DomainModel switch
    {
        AngledAirChannel a => a.Normal,
        PaintedAirChannel p => p.Path.Count > 1 ? Vector3.Normalize(p.Path[1] - p.Path[0]) : Vector3.UnitZ,
        _ => Vector3.UnitZ
    };
}
