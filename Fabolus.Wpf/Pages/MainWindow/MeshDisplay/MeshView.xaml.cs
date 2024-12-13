using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Mouse;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.Wpf.SharpDX.Elements2D;
using HelixToolkit.Wpf.SharpDX.Model.Scene;
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

namespace Fabolus.Wpf.Pages.MainWindow.MeshDisplay
{
    /// <summary>
    /// Interaction logic for MeshView.xaml
    /// </summary>
    public partial class MeshView : UserControl
    {
        public MeshView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Viewport3DX only performs mousemove events on mouse button, not while moving normally
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void view1_MouseMove(object sender, MouseEventArgs e) {
            var view = (Viewport3DX)sender;
            var hits = new List<HelixToolkit.Wpf.SharpDX.HitTestResult>();
            view.FindHits(e.GetPosition(view).ToVector2(), ref hits);
            var currentHit = hits.FirstOrDefault(x => x.IsValid);

            WeakReferenceMessenger.Default.Send(new MouseMoveMessage(hits, e));
        }
    }
}
