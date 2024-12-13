using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using static HelixToolkit.Wpf.SharpDX.Geometry3D;

namespace Fabolus.Wpf.Common.Mouse;
public sealed record MouseMoveMessage(List<HitTestResult> Hits, InputEventArgs OriginalEventArgs);
