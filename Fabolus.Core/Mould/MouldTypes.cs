using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Mould;
public enum MouldTypes {
    [Description("Simple")] Simple,
    [Description("Contoured")] Contoured,
    [Description("Wide-Based Contour")] ContouredWideBase,
}
