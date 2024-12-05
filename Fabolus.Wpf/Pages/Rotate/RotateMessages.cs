using SharpDX;

namespace Fabolus.Wpf.Pages.Rotate;

public sealed record ApplyTempRotationMessage(Vector3 axis, float angle);
