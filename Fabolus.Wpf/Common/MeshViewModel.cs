using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace Fabolus.Wpf.Common;
public class MeshViewModel : MeshViewModelBase 
{
    public MeshViewModel(bool? zoom = false) : base(zoom)
    {
        //messages
        WeakReferenceMessenger.Default.UnregisterAll(this);
        //WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => { Update(m.bolus.Geometry); });

        //BolusModel bolus = WeakReferenceMessenger.Default.Send<BolusRequestMessage>();
        //Update(bolus.Geometry);
    }

    private void Update(MeshGeometry3D bolus) {
        DisplayMesh.Children.Clear();

        if (bolus is null || bolus.TriangleIndices.Count < 1) { return; }

        //building geometry model
        var model = MeshSkins.SkinModel(bolus, MeshSkins.Bolus);
        DisplayMesh.Children.Add(model);
    }
}

