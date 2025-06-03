using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Mouse;
using HelixToolkit.Wpf.SharpDX;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HitTestResult = HelixToolkit.Wpf.SharpDX.HitTestResult;

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
            WeakReferenceMessenger.Default.Register<MeshView, ViewportRequestMessage>(this, (r, m) => m.Reply(r.view1));
        }

        /// <summary>
        /// Viewport3DX only performs mousemove events on mouse button, not while moving normally
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void view1_MouseMove(object sender, MouseEventArgs e) =>
            WeakReferenceMessenger.Default.Send(new MeshMouseMoveMessage(Hits(sender, e.GetPosition((Viewport3DX)sender)), e));
        

        private void view1_MouseUp(object sender, MouseButtonEventArgs e) {
            WeakReferenceMessenger.Default.Send(new MeshMouseUpMessage(Hits(sender, e.GetPosition((Viewport3DX)sender)), e));
        }

        private List<HitTestResult> Hits(object sender, Point pt) {
            var view = (Viewport3DX)sender;
            var hits = new List<HitTestResult>();
            view.FindHits(pt.ToVector2(), ref hits);
            return hits;
        }

        private void view1_MouseDown3D(object sender, RoutedEventArgs e) {
            var args = e as MouseDown3DEventArgs;
            WeakReferenceMessenger.Default.Send(new MeshMouseDownMessage(Hits(sender, args.Position), args.OriginalInputEventArgs));
        }

    }
}
