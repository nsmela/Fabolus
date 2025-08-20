using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using System.Windows;

namespace Fabolus.Wpf.Common.Scene;

public abstract class SceneManagerBase : ObservableObject, IDisposable {
    protected IMessenger Messenger { get; } = WeakReferenceMessenger.Default;

    protected abstract void RegisterMessages();

    protected virtual void RegisterInputBindings() =>
        Messenger.Send(new MeshDisplayInputsMessage(MeshDisplay.DefaultBindings));

    public virtual void Dispose() => Messenger.UnregisterAll(this);

    protected static void ShowErrorMessage(string title, string message) => MessageBox.Show(message, title);

}
