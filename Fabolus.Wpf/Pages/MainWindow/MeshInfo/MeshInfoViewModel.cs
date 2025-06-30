using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace Fabolus.Wpf.Pages.MainWindow.MeshInfo;

public partial class MeshInfoViewModel : ObservableObject {
    [ObservableProperty] private bool _isVisible = false;
    [ObservableProperty] private string _meshInfoText = string.Empty;

    public MeshInfoViewModel() {
        //messages
        WeakReferenceMessenger.Default.Register<MeshInfoSetMessage>(this, (r,m) => SetText(m.Text));
    }

    public void SetText(string text) {
        MeshInfoText = text;
        IsVisible = !string.IsNullOrEmpty(text);
    }
}
