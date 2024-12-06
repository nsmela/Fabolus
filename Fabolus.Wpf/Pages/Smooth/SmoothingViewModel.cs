using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Scene;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Smooth;

public class SmoothingViewModel : BaseViewModel {
    public override string TitleText => "Smooth";

    public override SceneManager GetSceneManager => new SmoothSceneManager();

    #region Properties and their Events

    //list of smoothing settings to use with the slider
    //default values
    private List<PoissonSmoothModel> _defaultSmoothSettings = new List<PoissonSmoothModel> {
            new PoissonSmoothModel{ Name = "rough", Depth = 9, Scale = 1.8f, SamplesPerNode = 2, EdgeLength = 1.0f },
            new PoissonSmoothModel{ Name = "standard", Depth = 9, Scale = 1.8f, SamplesPerNode = 1, EdgeLength = 0.6f },
            new PoissonSmoothModel{ Name = "smooth", Depth = 8, Scale = 1.4f, SamplesPerNode = 4, EdgeLength = 0.4f }
        };

    private PoissonSmoothModel SmoothingDefault => _defaultSmoothSettings[SmoothingIndex];
    private PoissonSmoothModel _smoothModel;
    private bool _isFrozen;

    [ObservableProperty] private string _smoothingLabel;
    [ObservableProperty] private int _smoothingIndex;
    [ObservableProperty] private int _depth, _samplesPerNode;
    [ObservableProperty] private float _smoothScale, _edgeLength;
    [ObservableProperty] private bool _advancedMode = false;

    partial void OnSmoothingIndexChanged(int value) {
        if (_isFrozen)
            return;
        _isFrozen = true; //prevents loops continuously updating these values

        _smoothModel.Name = SmoothingDefault.Name;
        _smoothModel.Depth = SmoothingDefault.Depth;
        _smoothModel.SamplesPerNode = SmoothingDefault.SamplesPerNode;
        _smoothModel.Scale = SmoothingDefault.Scale;
        _smoothModel.EdgeLength = SmoothingDefault.EdgeLength;

        SmoothingLabel = _smoothModel.Name;
        Depth = _smoothModel.Depth;
        SamplesPerNode = _smoothModel.SamplesPerNode;
        SmoothScale = _smoothModel.Scale;
        EdgeLength = _smoothModel.EdgeLength;

        ClearSmoothed();//removes the old smoothed mesh

        _isFrozen = false;
    }
    partial void OnDepthChanged(int value) => UpdateSettings();
    partial void OnSamplesPerNodeChanged(int value) => UpdateSettings();
    partial void OnSmoothScaleChanged(float value) => UpdateSettings();
    partial void OnEdgeLengthChanged(float value) => UpdateSettings();

    #endregion

    private BolusModel _bolus;
    public SmoothingViewModel() {
        _bolus = new BolusModel();
        _smoothModel = new();
        _isFrozen = false;

        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => { _bolus = m.bolus; });

        _bolus = WeakReferenceMessenger.Default.Send<BolusRequestMessage>();

        SmoothingIndex = 1; //starts at standard
        _smoothModel.Initialize(_bolus.RawMesh);

    }

    private void UpdateSettings() {
        if (_isFrozen)
            return;
        _isFrozen = true; //prevents loops continuously updating these values

        //indicate the settings are customized
        _smoothModel.Depth = Depth;
        _smoothModel.SamplesPerNode = SamplesPerNode;
        _smoothModel.Scale = SmoothScale;
        _smoothModel.EdgeLength = EdgeLength;

        _isFrozen = false;
    }

    #region Commands
    [RelayCommand]
    public async Task Smooth() {
        if (_bolus.Mesh == null)
            return; //no bolus to smooth

        ClearSmoothed();//removes the old smoothed mesh

        DMesh3 mesh = await Task.Run(() => _smoothModel.ToMesh());

        WeakReferenceMessenger.Default.Send(new AddNewBolusMessage(BolusModel.SMOOTHED_BOLUS_LABEL, mesh));
    }

    [RelayCommand] private void ClearSmoothed() => WeakReferenceMessenger.Default.Send(new RemoveBolusMessage(BolusModel.SMOOTHED_BOLUS_LABEL));
    [RelayCommand] private void ToggleAdvancedMode() => AdvancedMode = !_advancedMode;

    #endregion

}
}
