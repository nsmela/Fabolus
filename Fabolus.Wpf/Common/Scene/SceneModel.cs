using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Bolus;
using Fabolus.Wpf.Common.Mesh;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.Wpf.SharpDX.Model;
using HelixToolkit.Wpf.SharpDX.Model.Scene;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using static Fabolus.Wpf.Stores.BolusStore;
using Geometry3D = HelixToolkit.Wpf.SharpDX.Geometry3D;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;

namespace Fabolus.Wpf.Common.Scene;

public class SceneModel : IDisposable   {

    protected MeshGeometry3D? _model;
    protected Matrix _transform;

    public SceneModel() {
        //messaging
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this,  (r, m) => UpdateModel(m.bolus));

        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage()).Response;
        _model = bolus is not null ? bolus.Geometry : null;
        _transform = bolus is not null ? bolus.TransformMatrix : MeshHelper.TransformEmpty.ToMatrix();
        UpdateModel(bolus);

    }

    protected virtual void UpdateModel(BolusModel? bolus) {
        if (bolus is null) {
            _model = null;
            _transform = MeshHelper.TransformEmpty.ToMatrix();
            UpdateScene();
            return;
        }

        _model = bolus.Geometry;
        _transform = bolus.TransformMatrix;

        UpdateScene();
    }

    protected virtual void UpdateScene() {
        if (_model is null) {
            WeakReferenceMessenger.Default.Send(new MeshUpdatedMessage([]));
            return;
        }

        var model = new Object3D {Geometry = _model, Transform = [_transform] };

        WeakReferenceMessenger.Default.Send(new MeshUpdatedMessage([model]));
    }

    public void Dispose() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}

