using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Mould.Builders;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Features.Mould;
using Fabolus.Wpf.Pages.MainWindow;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Mould;

public partial class MouldViewModel : BaseViewModel {
    public override string TitleText => "mould";

    private BolusModel _bolus;
    private AirChannelsCollection _channels;
    private TriangulatedMouldGenerator _generator;

    [ObservableProperty] private double _bottomOffset;
    [ObservableProperty] private double _topOffset;
    [ObservableProperty] private double _widthOffset;
    [ObservableProperty] private bool _isTight;
    [ObservableProperty] private bool _hasTrough;

    private bool _isBusy = false;

    partial void OnBottomOffsetChanged(double value) {
        _generator = _generator.WithBottomOffset(value);
        ApplySettingsToGenerator();
    }

    partial void OnTopOffsetChanged(double value) {
        _generator = _generator.WithTopOffset(value);
        ApplySettingsToGenerator();
    }

    partial void OnWidthOffsetChanged(double value) {
        _generator = _generator.WithXYOffsets(value);
        ApplySettingsToGenerator();
    }

    partial void OnIsTightChanged(bool value) {
        _generator = _generator.WithTightContour(value);
        ApplySettingsToGenerator();
    }

    partial void OnHasTroughChanged(bool value) {
        _generator = _generator.WithTrough(value);
        ApplySettingsToGenerator();
    }

    private void ApplySettingsToGenerator() {
        if (_isBusy) { return; }
        _isBusy = true;

        GenerateMould();

        _isBusy = false;
    }

    public MouldViewModel() : base(new MouldSceneManager()) {
        _generator = _messenger.Send<MouldGeneratorRequest>().Response as TriangulatedMouldGenerator;
        if (_generator is null) { _generator = TriangulatedMouldGenerator.New(); }

        _bolus = _messenger.Send<BolusRequestMessage>().Response;
        _channels = _messenger.Send<AirChannelsRequestMessage>().Response;
        _generator = _generator
            .WithBolus(_bolus.TransformedMesh())
            .WithToolMeshes(_channels.Values.Select(c => c.Geometry.ToMeshModel()).ToArray())
            .WithContour(new()); //clears existing contour

        _isBusy = true;

        BottomOffset = _generator.OffsetBottom;
        TopOffset = _generator.OffsetTop;
        WidthOffset = _generator.OffsetXY;
        IsTight = _generator.IsTight;
        HasTrough = _generator.HasTrough;

        _messenger.Send(new MouldGeneratorUpdatedMessage(_generator));
        _isBusy = false;

        GenerateMould();
    }

    private void UpdateMeshInfo() {
        // bolus volume calculation
        BolusModel bolus = _messenger.Send(new BolusRequestMessage());

        MouldModel mould = _messenger.Send(new MouldRequestMessage());
        _messenger.Send(new MeshInfoSetMessage($"Bolus Volume:\r\n {bolus.Mesh.VolumeString()}\r\nMould Volume:\r\n {mould.VolumeString()}"));
    }

    [RelayCommand]
    private async Task GenerateMould() {
        var mould = new MouldModel(_generator, false);
        _messenger.Send(new MouldUpdatedMessage(mould));
        UpdateMeshInfo();
    }

    protected override void RegisterMessages() {
        
    }
}
