using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace Fabolus.Wpf.Features.Main;

public abstract partial class MeshInfoItem : ObservableObject
{
    [ObservableProperty] private string _label = string.Empty;
}

// label + value, e.g. "Triangles  184,204"
public partial class TextInfoItem : MeshInfoItem {
    [ObservableProperty] private string _value = string.Empty;
}

public partial class StatusInfoItem : MeshInfoItem {
    [ObservableProperty] private Color _colour = Colors.White;
    [ObservableProperty] private string _text = string.Empty;
}

// label/caption + progress bar
public partial class ProgressInfoItem : MeshInfoItem {
    [ObservableProperty] private double _value;          // 0..Maximum
    [ObservableProperty] private double _maximum = 100;
    [ObservableProperty] private string _caption = string.Empty;
}

public partial class TitleInfoItem : MeshInfoItem { }