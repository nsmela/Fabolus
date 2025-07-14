using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Mould.Builders;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Features.Mould;
using Fabolus.Wpf.Pages.MainWindow;
using Fabolus.Wpf.Pages.Mould.Views;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Mould;

public partial class MouldViewModel : BaseViewModel {
    public override string TitleText => "mould";

    public override SceneManager GetSceneManager => new MouldSceneManager();

    [ObservableProperty] private BaseMouldView? _currentMouldViewModel;

    private BolusModel? Bolus { get; set; }

    public MouldViewModel() {
        CurrentMouldViewModel = new SimpleMouldViewModel();
    }

}
