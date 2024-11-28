using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;

namespace Fabolus.Wpf.Common.ControlsModel;

public class BaseViewportInputBindings
{
    public MouseBinding LeftMouse => new MouseBinding { Command = ViewportCommands.Pan };
    public MouseBinding MiddleMouse => new MouseBinding { Command = ViewportCommands.Zoom };
    public MouseBinding RightMouse => new MouseBinding { Command= ViewportCommands.Rotate };
}

