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

    private Object3D? _model;
    private Matrix _transform;

    public SceneModel() {
        //messaging
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this,  (r, m) => UpdateModel(m.bolus));
        WeakReferenceMessenger.Default.Register<RotationUpdatedMessage>(this, (r, m) => UpdateRotation(m.transform));

        //set initial values
        var transform = WeakReferenceMessenger.Default.Send(new RotationRequestMessage()).Response;
        _transform = transform is not null ? transform.ToMatrix() : new ScaleTransform3D(1.0, 1.0, 1.0).ToMatrix();

        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage()).Response;
        _model = bolus is not null ? new Object3D { Geometry = bolus.Geometry} : null;
        UpdateModel(bolus);

    }

    protected virtual void UpdateModel(BolusModel? bolus) {
        if (bolus is null) {
            _model = null;
            UpdateScene();
            return;
        }

        var model = new Object3D();
        model.Geometry = bolus is not null ? bolus.Geometry : new MeshGeometry3D();
        model.Geometry.UpdateOctree();
        _model = model;

        UpdateScene();
    }

    protected virtual void UpdateRotation(Transform3D transform) {
        _transform = transform.ToMatrix();

        UpdateScene();
    }

    protected virtual void UpdateScene() {
        if (_model is null) {
            WeakReferenceMessenger.Default.Send(new MeshUpdatedMessage([]));
            return;
        }

        _model.Transform = new() { _transform };

        WeakReferenceMessenger.Default.Send(new MeshUpdatedMessage([_model]));
    }

    public void Dispose() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}

