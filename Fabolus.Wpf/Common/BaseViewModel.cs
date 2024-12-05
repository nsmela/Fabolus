using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Mesh;
using SceneManager = Fabolus.Wpf.Common.Scene.SceneManager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Common;
public abstract class BaseViewModel : ObservableObject, IDisposable {
    public abstract string TitleText { get; }

    public abstract SceneManager GetSceneModel { get; }

    public void Dispose() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
