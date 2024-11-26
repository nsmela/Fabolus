using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Common;
public abstract class ViewModelBase : ObservableObject {
    public virtual string TitleText { get; } = string.Empty;


}
