using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Bolus;
using Fabolus.Core.Common;
using Fabolus.Wpf.Common.Scene;
using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using MeshHelper = Fabolus.Wpf.Common.Mesh.MeshHelper;
using static Fabolus.Wpf.Stores.BolusStore;

namespace Fabolus.Wpf.Pages.Rotate;
public sealed class RotateSceneModel : SceneModel {

    public event Action<Object3D> SceneUpdated;

    private BolusModel _bolus;
    private Transform3D _transform = MeshHelper.TransformEmpty;

    public RotateSceneModel() {
        try {
            WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, async (r, m) => await UpdateModel(m.bolus));
            WeakReferenceMessenger.Default.Register<RotationUpdatedMessage>(this, async (r, m) => await UpdateModel(m.transform));
        } catch { }

        //setup
        _transform = WeakReferenceMessenger.Default.Send<RotationRequestMessage>().Response;
        _bolus = WeakReferenceMessenger.Default.Send<BolusRequestMessage>().Response;
        UpdateModel();
    }

    protected override async Task UpdateModel(BolusModel bolus) {
        _transform = WeakReferenceMessenger.Default.Send<RotationRequestMessage>().Response;
        _bolus = bolus;
        await UpdateModel();
    }

    private async Task UpdateModel(Transform3D transform) {
        _transform = transform;
        await UpdateModel();
    }

    private async Task UpdateModel() {
        var model = new Object3D();
        model.Geometry = _bolus.Geometry;
        model.Transform = new() { _transform.ToMatrix() };

        SceneUpdated?.Invoke(model);
    }
}
