using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.AirChannel;

public enum ChannelTypes {
    [Description("Straight")] Straight,
    [Description("Angled")] AngledHead,
}
