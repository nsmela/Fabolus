using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using System.Windows;
using SceneManager = Fabolus.Wpf.Common.Scene.SceneManager;


namespace Fabolus.Wpf.Common;
public abstract class BaseViewModel : ObservableObject, IDisposable {
    public abstract string TitleText { get; }

    public abstract SceneManager GetSceneManager { get; }

    public void Dispose() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    protected void ErrorMessage(string title, string message) {
        MessageBox.Show(message, title);
    }
}
