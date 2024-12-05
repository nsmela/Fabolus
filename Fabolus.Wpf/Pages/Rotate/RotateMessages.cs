using System.Windows.Media.Media3D;

namespace Fabolus.Wpf.Pages.Rotate;

public sealed record ApplyTempRotationMessage(Vector3D axis, float angle);
