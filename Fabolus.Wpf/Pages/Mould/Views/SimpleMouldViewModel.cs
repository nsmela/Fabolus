using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Mould.Builders;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Features.Mould;
using Fabolus.Wpf.Pages.MainWindow;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Mould.Views;

public partial class SimpleMouldViewModel : BaseMouldView {
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

        WeakReferenceMessenger.Default.Send(new MouldGeneratorUpdatedMessage(_generator));

        _isBusy = false;
    }

    private void UpdateMeshInfo() {
        // bolus volume calculation
        BolusModel bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());

        MouldModel mould = WeakReferenceMessenger.Default.Send(new MouldRequestMessage());
        WeakReferenceMessenger.Default.Send(new MeshInfoSetMessage($"Bolus Volume:\r\n {bolus.Mesh.VolumeString()}\r\nMould Volume:\r\n {mould.VolumeString()}"));
    }

    public SimpleMouldViewModel() {
        _generator = WeakReferenceMessenger.Default.Send<MouldGeneratorRequest>().Response as TriangulatedMouldGenerator;
        if (_generator is null) { _generator = TriangulatedMouldGenerator.New(); }

        var bolus = WeakReferenceMessenger.Default.Send<BolusRequestMessage>().Response;
        var channels = WeakReferenceMessenger.Default.Send<AirChannelsRequestMessage>().Response;
        _generator = _generator
            .WithBolus(bolus.TransformedMesh())
            .WithToolMeshes(channels.Values.Select(c => c.Geometry.ToMeshModel()).ToArray())
            .WithContour(new()); //clears existing contour

        _isBusy = true;

        BottomOffset = _generator.OffsetBottom;
        TopOffset = _generator.OffsetTop;
        WidthOffset = _generator.OffsetXY;
        IsTight = _generator.IsTight;
        HasTrough = _generator.HasTrough;

        WeakReferenceMessenger.Default.Send(new MouldGeneratorUpdatedMessage(_generator));
        _isBusy = false;

        UpdateMeshInfo();
    }

    [RelayCommand]
    private async Task GenerateMould() {
        var mould = new MouldModel(_generator, false);
        WeakReferenceMessenger.Default.Send(new MouldUpdatedMessage(mould));
        UpdateMeshInfo();
    }
}
