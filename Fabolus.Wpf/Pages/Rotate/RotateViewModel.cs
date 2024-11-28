using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Mesh;
using SharpDX;
using Vector3D = System.Windows.Media.Media3D.Vector3D;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Fabolus.Wpf.Stores.BolusStore;

namespace Fabolus.Wpf.Pages.Rotate;
public partial class RotateViewModel : BaseViewModel {
    public override string TitleText => "Rotation";

    public override BaseMeshViewModel GetMeshViewModel(BaseMeshViewModel? meshViewModel) => new RotateMeshViewModel(meshViewModel);

    private bool _isLocked = false;
    [ObservableProperty] private float _xAxisAngle;
    [ObservableProperty] private float _yAxisAngle;
    [ObservableProperty] private float _zAxisAngle;
    [ObservableProperty] private float _lowerOverhang;
    [ObservableProperty] private float _upperOverhang;

    partial void OnXAxisAngleChanged(float value) => SendTempRotation(MeshHelper.VectorXAxis, value);
    partial void OnYAxisAngleChanged(float value) => SendTempRotation(MeshHelper.VectorYAxis, value);
    partial void OnZAxisAngleChanged(float value) => SendTempRotation(MeshHelper.VectorZAxis, value);

    private void ResetValues() {
        _isLocked = true;
        //setting slider values
        XAxisAngle = 0.0f;
        YAxisAngle = 0.0f;
        ZAxisAngle = 0.0f;
        _isLocked = false;

    }

    private void SendTempRotation(Vector3D axis, float angle) {
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
        Vector3D axis;
        var angle = 0.0f;

        //selecting the axis that was changed
        if (XAxisAngle != 0) {
            axis = MeshHelper.VectorXAxis;
            angle = XAxisAngle;
        }

        if (YAxisAngle != 0) {
            axis = MeshHelper.VectorYAxis;
            angle = YAxisAngle;
        }

        if (ZAxisAngle != 0) {
            axis = MeshHelper.VectorZAxis;
            angle = ZAxisAngle;
        }

        ResetValues();
        WeakReferenceMessenger.Default.Send(new ApplyRotationMessage(axis, angle));
    }
    #endregion
}
