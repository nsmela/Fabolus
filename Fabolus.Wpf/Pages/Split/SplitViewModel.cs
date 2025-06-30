using CommunityToolkit.Mvvm.ComponentModel;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Scene;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Pages.Split;

public partial class SplitViewModel : BaseViewModel {
    public override string TitleText => "split";

    public override SceneManager GetSceneManager => new SplitSceneManager();


    [ObservableProperty] private int _smoothnessDegree = 5;



}
