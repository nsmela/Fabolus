using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Scene;
using System.Windows;


namespace Fabolus.Wpf.Common;

public abstract class BaseViewModel(SceneManagerBase sceneManager) : ObservableObject, IDisposable {

    // displayed in the main view to show which tool view is active
    public abstract string TitleText { get; }
    protected IMessenger Messenger { get; } = WeakReferenceMessenger.Default;
    protected SceneManagerBase SceneManager { get; } = sceneManager; // handles the mesh view while using this view model
    protected abstract void RegisterMessages();

    public virtual void Dispose() {
        Messenger.UnregisterAll(this); // prevents multiple views from listenign at the same time
        SceneManager.Dispose(); // prevents multiple scene managers from influcencing the mesh view
    }

    protected static void ShowErrorMessage(string title, string message) => MessageBox.Show(message, title);
}
