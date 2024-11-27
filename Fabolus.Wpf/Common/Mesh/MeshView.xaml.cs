using System.Windows.Controls;


namespace Fabolus.Wpf.Common.Mesh;
/// <summary>
/// Interaction logic for MeshView.xaml
/// </summary>
public partial class MeshView : UserControl {
    public MeshView() {
        DataContext = new MeshViewModel();
        InitializeComponent();
    }
}
