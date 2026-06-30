using System.ComponentModel;

namespace Fabolus.Wpf.Features.Smoothing;

public enum SmoothDisplayMode
{
    [Description("None")] None,
    [Description("Cross Section")] CrossSection,
    [Description("Heat Map")] Heatmap
}
