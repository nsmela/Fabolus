using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Fabolus.Wpf.Pages.Import;
/// <summary>
/// Interaction logic for ImportMeshView.xaml
/// </summary>
public partial class ImportMeshView : UserControl {
    public ImportMeshView() {
        DataContext = new ImportMeshViewModel(null);
        InitializeComponent();
    }
}
