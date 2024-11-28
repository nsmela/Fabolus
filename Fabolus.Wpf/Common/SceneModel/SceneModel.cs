using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Bolus;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.Wpf.SharpDX.Model;
using HelixToolkit.Wpf.SharpDX.Model.Scene;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Fabolus.Wpf.Stores.BolusStore;

namespace Fabolus.Wpf.Common.SceneModel;

public class SceneModel {
    public event Action<SceneNodeGroupModel3D> SceneUpdated;

    public SceneModel() {
        //messaging
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => UpdateScene(m.bolus));
    }

    private async void UpdateScene(BolusModel bolus) {
        var transform = WeakReferenceMessenger.Default.Send<RotationRequestMessage>().Response;
        var scene = new SceneNodeGroupModel3D();
        scene.AddNode(new MeshNode() { Geometry = bolus.Geometry, ModelMatrix = transform.ToMatrix() });

        SceneUpdated?.Invoke(scene);
    }

}

