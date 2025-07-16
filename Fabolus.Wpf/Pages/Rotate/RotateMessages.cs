using SharpDX;

namespace Fabolus.Wpf.Pages.Rotate;

public sealed record ApplyTempRotationMessage(Vector3 Axis, float Angle);
public sealed record ApplyOverhangSettings(float UpperAngle, float LowerAngle);
public sealed record ShowActiveRotationMessage(Vector3 axis); // shows/hides the rotation widget
