using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Common.Helpers;
using Fabolus.Wpf.Features.Channels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features;
public class AirChannelSettings : Dictionary<ChannelTypes, IAirChannel> {
    public ChannelTypes SelectedType { get; private set; }
    public IAirChannel NewChannel() => this[SelectedType].New();
    
    public static AirChannelSettings Initialize() {
        var settings = new AirChannelSettings();

        foreach(var type in EnumHelper.GetEnums<ChannelTypes>()) {
            settings.Add(type, type.ToAirChannel());
        }

        if (settings is null) { throw new ArgumentNullException("Failed to initialize AirChannelSettings"); }
        settings.SelectedType = (ChannelTypes)0; //first option

        return settings;
    }

    public void Reset() {
        this.Clear();
    }

    public void SetSelectedType(ChannelTypes value) {
        SelectedType = value;
    }

    public AirChannelSettings Copy() {
        var settings = new AirChannelSettings();
        var values = this.Values;
        foreach (var (key, value) in this.Select(x => x)) {
            settings.Add(key, value);
        }
        return settings;
    } 
}
