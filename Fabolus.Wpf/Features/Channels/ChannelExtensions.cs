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
    public static AirChannel ToAirChannel(this ChannelTypes type) =>
        type switch {
            ChannelTypes.Straight => new StraightAirChannel(),
            ChannelTypes.AngledHead => new AngledAirChannel(),
            _ => throw new NotImplementedException($"{type.GetDescriptionString()} is not listed as a ChannelType")};

    public static BaseChannelsViewModel ToViewModel(this ChannelTypes type) =>
        type switch {
            ChannelTypes.Straight => new StraightChannelsViewModel(),
            ChannelTypes.AngledHead => new AngledChannelsViewModel(),
            _ => throw new NotImplementedException($"{type.GetDescriptionString()} is not listed as a ChannelType")};

    public static BaseChannelsViewModel ToViewModel(this ChannelTypes type, AirChannel? settings) =>
    type switch {
        ChannelTypes.Straight => new StraightChannelsViewModel(settings),
        ChannelTypes.AngledHead => new AngledChannelsViewModel(settings),
        _ => throw new NotImplementedException($"{type.GetDescriptionString()} is not listed as a ChannelType")
    };
}
