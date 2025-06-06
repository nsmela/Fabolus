using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Features.Channels.Angled;
using Fabolus.Wpf.Features.Channels.Straight;
using Fabolus.Wpf.Pages.Channels;
using Fabolus.Wpf.Pages.Channels.Angled;
using Fabolus.Wpf.Pages.Channels.Straight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.Channels;

public static class ChannelExtensions {
    public static IAirChannel ToAirChannel(this ChannelTypes type) =>
        type switch {
            ChannelTypes.Straight => new StraightAirChannel(),
            ChannelTypes.AngledHead => new AngledAirChannel(),
            _ => throw new NotImplementedException($"{type.GetDescriptionString()} is not listed as a ChannelType")};

    public static BaseChannelsViewModel ToViewModel(this ChannelTypes type) =>
        type switch {
            ChannelTypes.Straight => new StraightChannelsViewModel(),
            ChannelTypes.AngledHead => new AngledChannelsViewModel(),
            _ => throw new NotImplementedException($"{type.GetDescriptionString()} is not listed as a ChannelType")};

    public static IAirChannel ApplySettings(this IAirChannel channel, AirChannelSettings settings) => channel.ChannelType switch {
        ChannelTypes.Straight => (channel as StraightAirChannel).ApplySettings(settings),
        ChannelTypes.AngledHead => (channel as AngledAirChannel).ApplySettings(settings),
        _ => throw new NotImplementedException($"{channel.ChannelType.GetDescriptionString()} is not listed as a ChannelType")
    };
};

