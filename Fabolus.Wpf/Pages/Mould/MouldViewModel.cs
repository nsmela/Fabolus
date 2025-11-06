using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Mould;
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

public partial class MouldViewModel : BaseViewModel
{
    public override string TitleText => "mould";

    private BolusModel _bolus;
    private AirChannelsCollection _channels;

    // Current generator
    private MouldGenerator _generator;

    // settings for each mould type
    private TriangulatedMouldGenerator _triangulatedGenerator = TriangulatedMouldGenerator.New();
    private ContouredMouldGenerator _contouredGenerator = ContouredMouldGenerator.New();

    // --- Properties for Triangulated Generator ---
    [ObservableProperty] private double _bottomOffset;
    [ObservableProperty] private double _topOffset;
    [ObservableProperty] private double _widthOffset;
    [ObservableProperty] private bool _hasTrough;

    // --- Properties for Contoured Generator ---
    [ObservableProperty] private double _contouredOffset;

    // --- Mould Type Selection ---
    [ObservableProperty] private int _selectedMouldIndex;
    [ObservableProperty] private string[] _mouldTypeOptions = ["Tight Box", "Wide Box", "Contour"];

    private bool _isBusy = false;

    // --- Triangulated Handlers ---
    partial void OnBottomOffsetChanged(double value)
    {
        _triangulatedGenerator = _triangulatedGenerator.WithBottomOffset(value);
        ApplySettingsToGenerator();
    }

    partial void OnTopOffsetChanged(double value)
    {
        _triangulatedGenerator = _triangulatedGenerator.WithTopOffset(value);
        ApplySettingsToGenerator();
    }

    partial void OnWidthOffsetChanged(double value)
    {
        _triangulatedGenerator = _triangulatedGenerator.WithXYOffsets(value);
        ApplySettingsToGenerator();
    }

    partial void OnHasTroughChanged(bool value)
    {
        _triangulatedGenerator = _triangulatedGenerator.WithTrough(value);
        ApplySettingsToGenerator();
    }

    // partial void OnIsTightChanged(bool value) // REMOVED

    // --- Contoured Handler ---
    partial void OnContouredOffsetChanged(double value)
    {
        _contouredGenerator = _contouredGenerator.WithOffset(value);
        ApplySettingsToGenerator();
    }


    partial void OnSelectedMouldIndexChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue) { return; }
        ApplySettingsToGenerator();
    }

    private void ApplySettingsToGenerator()
    {
        if (_isBusy) { return; }
        _isBusy = true;

        var selectedMould = MouldTypeOptions[SelectedMouldIndex];

        switch (selectedMould)
        {
            case "Tight Box":
                _triangulatedGenerator = _triangulatedGenerator.WithTightContour(true);
                _generator = _triangulatedGenerator;
                break;
            case "Wide Box":
                _triangulatedGenerator = _triangulatedGenerator.WithTightContour(false);
                _generator = _triangulatedGenerator;
                break;
            case "Contour":
                _generator = _contouredGenerator;
                break;
            default:
                _generator = _triangulatedGenerator;
                break;
        }

        GenerateMould();

        _isBusy = false;
    }

    public MouldViewModel() : base(new MouldSceneManager())
    {
        var storedGenerator = _messenger.Send<MouldGeneratorRequest>().Response;

        // Initialize both generator types
        if (storedGenerator is TriangulatedMouldGenerator triGen)
        {
            _triangulatedGenerator = triGen;
            _contouredGenerator = ContouredMouldGenerator.New();
        } else if (storedGenerator is ContouredMouldGenerator conGen)
        {
            _contouredGenerator = conGen;
            _triangulatedGenerator = TriangulatedMouldGenerator.New();
        } else
        {
            // Default: create new
            _triangulatedGenerator = TriangulatedMouldGenerator.New();
            _contouredGenerator = ContouredMouldGenerator.New();
        }

        _bolus = _messenger.Send<BolusRequestMessage>().Response;
        _channels = _messenger.Send<AirChannelsRequestMessage>().Response;
        var toolMeshes = _channels.Values.Select(c => c.Geometry.ToMeshModel()).ToArray();
        _triangulatedGenerator = _triangulatedGenerator
            .WithBolus(_bolus.TransformedMesh())
            .WithToolMeshes(toolMeshes)
            .WithContour(new()); //clears existing contour

        _contouredGenerator = _contouredGenerator
            .WithBolus(_bolus.TransformedMesh())
            .WithToolMeshes(toolMeshes);

        _isBusy = true;

        // Set VM properties from Triangulated
        BottomOffset = _triangulatedGenerator.OffsetBottom;
        TopOffset = _triangulatedGenerator.OffsetTop;
        WidthOffset = _triangulatedGenerator.OffsetXY;
        HasTrough = _triangulatedGenerator.HasTrough;

        // Set VM properties from Contoured
        ContouredOffset = _contouredGenerator.Offset;

        // Set initial mould type based on loaded generator
        if (storedGenerator is ContouredMouldGenerator)
        {
            SelectedMouldIndex = 2;
        } else if (_triangulatedGenerator.IsTight)
        {
            SelectedMouldIndex = 0;
        } else
        {
            SelectedMouldIndex = 1;
        }

        _messenger.Send(new MouldGeneratorUpdatedMessage(_generator));
        _isBusy = false;

        GenerateMould();
    }

    private void UpdateMeshInfo()
    {
        // bolus volume calculation
        BolusModel bolus = _messenger.Send(new BolusRequestMessage());

        MouldModel mould = _messenger.Send(new MouldRequestMessage());
        _messenger.Send(new MeshInfoSetMessage($"Bolus Volume:\r\n {bolus.Mesh.VolumeString()}\r\nMould Volume:\r\n {mould.VolumeString()}"));
    }

    private async Task GenerateMould()
    {
        var mould = new MouldModel(_generator, false);
        _messenger.Send(new MouldUpdatedMessage(mould));
        UpdateMeshInfo();
    }

    protected override void RegisterMessages()
    {

    }
}
