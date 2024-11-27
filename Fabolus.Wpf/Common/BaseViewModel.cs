using CommunityToolkit.Mvvm.ComponentModel;
using Fabolus.Wpf.Common.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Common;
public abstract class BaseViewModel : ObservableObject {
    public virtual string TitleText { get; } = string.Empty;
    public virtual BaseMeshViewModel MeshViewModel { get; private set; } = new MeshViewModel();
}
