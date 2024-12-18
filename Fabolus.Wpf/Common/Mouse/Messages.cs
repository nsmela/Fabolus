using HelixToolkit.Wpf.SharpDX;
using System.Windows.Input;

namespace Fabolus.Wpf.Common.Mouse;

public sealed record MouseMoveMessage(List<HitTestResult> Hits, InputEventArgs OriginalEventArgs);
