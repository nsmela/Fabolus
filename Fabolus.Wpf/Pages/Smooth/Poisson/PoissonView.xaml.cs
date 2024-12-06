using System.Windows.Controls;


namespace Fabolus.Wpf.Pages.Smooth.Poisson;
/// <summary>
/// Interaction logic for PoissonView.xaml
/// </summary>
public partial class PoissonView : UserControl {
    public PoissonView() {
        DataContext = new PoissonViewModel();
        InitializeComponent();
    }
}
