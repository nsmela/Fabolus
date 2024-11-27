using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Common;
public abstract class BaseViewModel : ObservableObject, IDisposable {
    public abstract string TitleText { get; }
    public abstract BaseMeshViewModel GetMeshViewModel(BaseMeshViewModel? meshViewModel);

    public void Dispose() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
