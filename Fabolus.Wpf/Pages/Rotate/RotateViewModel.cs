using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using SharpDX;
using Vector3D = System.Windows.Media.Media3D.Vector3D;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Rotate;
public partial class RotateViewModel : BaseViewModel {
    public override string TitleText => "Rotation";

    public override SceneManager GetSceneModel => new RotateSceneManager();

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
    #endregion

    #region Overhangs
    [ObservableProperty] private float _lowerOverhang;
    [ObservableProperty] private float _upperOverhang;
    private bool _isOverhangsFrozen = false;

    partial void OnLowerOverhangChanged(float value) => ApplyOverhangSettings();
    partial void OnUpperOverhangChanged(float value) => ApplyOverhangSettings();

    private void ApplyOverhangSettings() {
        if (_isOverhangsFrozen) { return; }
        _isOverhangsFrozen = true;

        WeakReferenceMessenger.Default.Send(new ApplyOverhangSettings(UpperOverhang, LowerOverhang));
        _isOverhangsFrozen = false;
    }
    #endregion
}
