using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Mould.Builders;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Features.Mould;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Mould.Views;

public partial class SimpleMouldViewModel : BaseMouldView {
    private BolusModel _bolus;
    private AirChannelsCollection _channels;

    [ObservableProperty] private double _bottomOffset;
    [ObservableProperty] private double _topOffset;
    [ObservableProperty] private double _widthOffset;
    [ObservableProperty] private double _resolution;

    private bool _isBusy = false;

    partial void OnBottomOffsetChanged(double value) => ApplySettingsToGenerator();
    partial void OnTopOffsetChanged(double value) => ApplySettingsToGenerator();
    partial void OnWidthOffsetChanged(double value) => ApplySettingsToGenerator();
    partial void OnResolutionChanged(double value) => ApplySettingsToGenerator();

    private void ApplySettingsToGenerator() {
        if (_isBusy) { return; }
        _isBusy = true;

        WeakReferenceMessenger.Default.Send(new MouldGeneratorUpdatedMessage(Generator));

        _isBusy = false;
    }

    private MouldGenerator Generator => TriangulatedMouldGenerator.New()
        .WithBolus(_bolus.TransformedMesh())
        .WithToolMeshes(_channels.Values.Select(c => c.Geometry.ToMeshModel()).ToArray())
        .WithBottomOffset(BottomOffset)
        .WithTopOffset(TopOffset)
        .WithXYOffsets(WidthOffset)
        .WithContourResolution(Resolution);

    public SimpleMouldViewModel() {
        _bolus = WeakReferenceMessenger.Default.Send<BolusRequestMessage>().Response as BolusModel;
        _channels = WeakReferenceMessenger.Default.Send<AirChannelsRequestMessage>().Response as AirChannelsCollection;

        var generator = WeakReferenceMessenger.Default.Send<MouldGeneratorRequest>().Response as TriangulatedMouldGenerator;
        if (generator is null) {
            generator = TriangulatedMouldGenerator.New(); 
        }

        generator = generator
            .WithBolus(_bolus.TransformedMesh())
            .WithToolMeshes(_channels.Values.Select(c => c.Geometry.ToMeshModel()).ToArray());

        UpdateSettings(generator);

    }

    protected void UpdateSettings(MouldGenerator? generator) {
        if (generator is null) {
            generator = TriangulatedMouldGenerator
                .New()
                .WithBolus(_bolus.TransformedMesh())
                .WithToolMeshes(_channels.Values.Select(c => c.Geometry.ToMeshModel()).ToArray());
        }

        _isBusy = true;

        BottomOffset = generator.OffsetBottom;
        TopOffset = generator.OffsetTop;
        WidthOffset = generator.OffsetXY;
        Resolution = generator.ContourResolution;

        WeakReferenceMessenger.Default.Send(new MouldGeneratorUpdatedMessage(generator));
        _isBusy = false;
    }

    [RelayCommand]
    private async Task GenerateMould() {
        _bolus = WeakReferenceMessenger.Default.Send<BolusRequestMessage>().Response as BolusModel;
        _channels = WeakReferenceMessenger.Default.Send<AirChannelsRequestMessage>().Response as AirChannelsCollection;

        var mould = new MouldModel(this.Generator, false);
        WeakReferenceMessenger.Default.Send(new MouldUpdatedMessage(mould));
    }
}
