using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Fabolus.Core.Bolus;
using Fabolus.Wpf.Common.Mesh;
using g3;
using HelixToolkit.Wpf.SharpDX;
using System.IO;
using System.Windows.Media.Media3D;


namespace Fabolus.Wpf.Stores;
public class BolusStore {

    #region Messages
    //importing file
    public sealed record AddBolusFromFileMessage(string filepath);
    public sealed record RemoveBolusMessage(string label);
    public sealed record ClearBolusMessage();

    //utility
    public sealed record BolusUpdatedMessage(BolusModel bolus);

    //rotations
    public sealed record ApplyTempRotationMessage(Vector3D axis, float angle);
    public sealed record ApplyRotationMessage(Vector3D axis, float angle);
    public sealed record ClearRotationsMessage();
    public class RotationRequestMessage : RequestMessage<Transform3D> { }

    //overhangs
    public sealed record ApplyOverhangSettingsMessage(float lower, float upper);

    //request messages
    public class BolusRequestMessage : RequestMessage<BolusModel> { }
    public class BolusFilePathRequestMessage : RequestMessage<string> { }
    //public class BolusOverhangMaterialRequestMessage : RequestMessage<DiffuseMaterial> { }
    public class BolusOverhangSettingsRequestMessage : RequestMessage<double[]> { }

    #endregion

    #region Fields and Properties

    private BolusModel _bolus;
    private Transform3D _transform;

    #endregion

    public BolusStore()
    {
        //default values
        _transform = MeshHelper.TransformEmpty; //an empty transform

        //registering messages
        WeakReferenceMessenger.Default.Register<AddBolusFromFileMessage>(this, async (r,m) => await BolusFromFile(m.filepath) );
        WeakReferenceMessenger.Default.Register<ApplyTempRotationMessage>(this, async (r, m) => await AddTempTransform(m.axis, m.angle));
        WeakReferenceMessenger.Default.Register<ApplyRotationMessage>(this, async (r, m) => await AddTransform(m.axis, m.angle));
        WeakReferenceMessenger.Default.Register<ClearRotationsMessage>(this, async (r, m) => await ClearTransforms());

        //request messages
        WeakReferenceMessenger.Default.Register<BolusStore, BolusRequestMessage>(this, (r, m) => m.Reply(r._bolus) );
        WeakReferenceMessenger.Default.Register<BolusStore, RotationRequestMessage>(this, (r, m) => m.Reply(r._transform) );
    }

    #region Messages
    private async Task AddTempTransform(Vector3D axis, float angle) {
        //stack transforms
        var transform = new Transform3DGroup { Children = [_transform, MeshHelper.TransformFromAxis(axis, angle)] };

        _bolus.TransformMatrix = transform.ToMatrix();
        //process overhangs

        await BolusUpdated();
    }

    private async Task AddTransform(Vector3D axis, float angle) {
        _transform = new Transform3DGroup { Children = [_transform, MeshHelper.TransformFromAxis(axis, angle)] };
        _bolus.TransformMatrix = _transform.ToMatrix();
        await BolusUpdated();
    }

    private async Task BolusFromFile(string filepath) {
        if (!File.Exists(filepath)) {
            System.Windows.MessageBox.Show("Unable to find: " + filepath);
            return;
        }

        var mesh = new DMesh3(await Task.Factory.StartNew(() => StandardMeshReader.ReadMesh(filepath)), false, true);

        //if mesh isn't good
        if (mesh is null) {
            System.Windows.MessageBox.Show(filepath + " was an invalid mesh!");
            return;
        }

        _bolus = new BolusModel(mesh);
        _bolus.TransformMatrix = _transform.ToMatrix();

        await BolusUpdated();
    }

    private async Task BolusUpdated() => WeakReferenceMessenger.Default.Send(new BolusUpdatedMessage(_bolus));

    private async Task ClearTransforms() {
        _transform = MeshHelper.TransformEmpty;
        _bolus.TransformMatrix = _transform.ToMatrix();

        await BolusUpdated();
    }

    #endregion
}
