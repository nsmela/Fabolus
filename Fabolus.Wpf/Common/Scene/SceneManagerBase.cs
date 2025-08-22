using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using System.Windows;

namespace Fabolus.Wpf.Common.Scene;

public abstract class SceneManagerBase : ObservableObject, IDisposable {
    protected readonly IMessenger _messenger = WeakReferenceMessenger.Default;

    protected abstract void RegisterMessages();

    protected virtual void RegisterInputBindings() =>
        _messenger.Send(new MeshDisplayInputsMessage(MeshDisplay.DefaultBindings));

    public virtual void Dispose() => _messenger.UnregisterAll(this);

    protected static void ShowErrorMessage(string title, string message) => MessageBox.Show(message, title);

}
