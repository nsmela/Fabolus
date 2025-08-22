using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Scene;
using System.Windows;


namespace Fabolus.Wpf.Common;

public abstract class BaseViewModel(SceneManagerBase sceneManager) : ObservableObject, IDisposable {

    // displayed in the main view to show which tool view is active
    public abstract string TitleText { get; }
    protected readonly IMessenger _messenger = WeakReferenceMessenger.Default;
    protected abstract void RegisterMessages();
    protected readonly SceneManagerBase _sceneManager = sceneManager; // handles the mesh view while using this view model

    /// <summary>
    /// Called when DataContextDisposal is set to true in the view.
    /// </summary>
    public virtual void Dispose() {
        _messenger.UnregisterAll(this); // prevents multiple views from listenign at the same time
        _sceneManager.Dispose(); // prevents multiple scene managers from influcencing the mesh view
    }

    protected static void ShowErrorMessage(string title, string message) => MessageBox.Show(message, title);
}
