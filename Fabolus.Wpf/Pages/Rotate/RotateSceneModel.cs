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
using SharpDX;
using Fabolus.Wpf.Common.Mesh;

namespace Fabolus.Wpf.Pages.Rotate;
public sealed class RotateSceneModel : SceneModel {

    public RotateSceneModel() {
        try {
            WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => UpdateModel(m.bolus));
        } catch { }

        //setup
        var bolus = WeakReferenceMessenger.Default.Send<BolusRequestMessage>().Response;
        _model = bolus.Geometry;
        _transform = bolus.TransformMatrix;

        UpdateModel();
    }

    protected override void UpdateModel(BolusModel bolus) {
        _transform = bolus.TransformMatrix;
        _model = bolus.Geometry;
        UpdateModel();
    }

    private void UpdateModel() {
        var model = new Object3D { Geometry = _model, Transform = [_transform] };

        WeakReferenceMessenger.Default.Send(new MeshUpdatedMessage([model]));
    }
}
