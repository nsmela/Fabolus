using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Mould;
public enum MouldTypes {
    [Description("Tight-Boxed")] BOX_TIGHT,
    [Description("Boxed")] BOX_WIDE,
    [Description("Contoured")] CONTOURED,
}
