using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.MainWindow;
using SharpDX;

using static Fabolus.Wpf.Bolus.BolusStore;
using static System.Net.Mime.MediaTypeNames;

namespace Fabolus.Wpf.Pages.Rotate;

public partial class RotateViewModel : BaseViewModel {
    public override string TitleText => "Rotation";

    private bool _isLocked = false;

    [ObservableProperty] private float _xAxisAngle;
    [ObservableProperty] private float _yAxisAngle;
    [ObservableProperty] private float _zAxisAngle;

    partial void OnXAxisAngleChanged(float value) => SendTempRotation(Vector3.UnitX, value);
    partial void OnYAxisAngleChanged(float value) => SendTempRotation(Vector3.UnitY, value);
    partial void OnZAxisAngleChanged(float value) => SendTempRotation(Vector3.UnitZ, value);

    private void ResetValues() {
        _isLocked = true;

        //setting slider values
        XAxisAngle = 0.0f;
        YAxisAngle = 0.0f;
        ZAxisAngle = 0.0f;
        _isLocked = false;
    }

    public RotateViewModel() : base(new RotateSceneManager()) {
        WeakReferenceMessenger.Default.Send(new MeshInfoSetMessage(string.Empty));
    }

    private void ShowAxisRotation(Vector3 axis) {
        WeakReferenceMessenger.Default.Send(new ShowActiveRotationMessage(axis));
    }

    private void SendTempRotation(Vector3 axis, float angle) {
        if (_isLocked) { return; }
        WeakReferenceMessenger.Default.Send(new ApplyTempRotationMessage(axis, angle));
    }

    #region Commands
    [RelayCommand]
    private void ClearRotation() {
        ResetValues();
        WeakReferenceMessenger.Default.Send(new ClearRotationsMessage());
    }

    [RelayCommand]
    private void SaveRotation() {
        Vector3 axis = Vector3.Zero;
        var angle = 0.0f;

        //selecting the axis that was changed
        if (XAxisAngle != 0) {
            axis = Vector3.UnitX;
            angle = XAxisAngle;
        }

        if (YAxisAngle != 0) {
            axis = Vector3.UnitY;
            angle = YAxisAngle;
        }

        if (ZAxisAngle != 0) {
            axis = Vector3.UnitZ;
            angle = ZAxisAngle;
        }

        ResetValues();
        WeakReferenceMessenger.Default.Send(new ApplyRotationMessage(axis, angle));
    }

    [RelayCommand]
    private void ShowAxisXRotation() => ShowAxisRotation(Vector3.UnitX);

    [RelayCommand]
    private void ShowAxisYRotation() => ShowAxisRotation(Vector3.UnitY);
    
    [RelayCommand]
    private void ShowAxisZRotation() => ShowAxisRotation(Vector3.UnitZ);
    
    [RelayCommand]
    private void HideAxisRotation() => ShowAxisRotation(Vector3.Zero);

    #endregion

    #region Overhangs
    [ObservableProperty] private float _lowerOverhang = RotateSceneManager.DEFAULT_OVERHANG_LOWER;
    [ObservableProperty] private float _upperOverhang = RotateSceneManager.DEFAULT_OVERHANG_UPPER;
    private bool _isOverhangsFrozen = false;

    partial void OnLowerOverhangChanged(float value) => ApplyOverhangSettings();
    partial void OnUpperOverhangChanged(float value) => ApplyOverhangSettings();

    private void ApplyOverhangSettings() {
        if (_isOverhangsFrozen) { return; }
        _isOverhangsFrozen = true;

        WeakReferenceMessenger.Default.Send(new ApplyOverhangSettings(UpperOverhang, LowerOverhang));
        _isOverhangsFrozen = false;
    }

    protected override void RegisterMessages() {
        throw new NotImplementedException();
    }
    #endregion
}
