using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Mesh;
using g3;
using HelixToolkit.Wpf.SharpDX;
using System.IO;
using System.Windows.Media.Media3D;

namespace Fabolus.Wpf.Bolus;
public class BolusStore {

    #region Messages
    //importing file
    public sealed record AddBolusFromFileMessage(string filepath);

    //utility
    public sealed record BolusUpdatedMessage(BolusModel bolus);

    //rotations
    public sealed record ApplyTempRotationMessage(Vector3D axis, float angle);
    public sealed record ApplyRotationMessage(Vector3D axis, float angle);
    public sealed record ClearRotationsMessage();

    //request messages
    public class BolusRequestMessage : RequestMessage<BolusModel> { }

    #endregion

    #region Fields and Properties

    private Dictionary<string, BolusModel> _boli = []; //for different models used
    private BolusModel _bolus;
    private BolusRotation _rotation = new();
    private BolusTransform _transform = new();

    #endregion

    public BolusStore()
    {

        //registering messages
        WeakReferenceMessenger.Default.Register<AddBolusFromFileMessage>(this, async (r,m) => await BolusFromFile(m.filepath) );
        WeakReferenceMessenger.Default.Register<ApplyTempRotationMessage>(this, async (r, m) => await AddTempTransform(m.axis, m.angle));
        WeakReferenceMessenger.Default.Register<ApplyRotationMessage>(this, async (r, m) => await AddTransform(m.axis, m.angle));
        WeakReferenceMessenger.Default.Register<ClearRotationsMessage>(this, async (r, m) => await ClearTransforms());

        //request messages
        WeakReferenceMessenger.Default.Register<BolusStore, BolusRequestMessage>(this, (r, m) => m.Reply(r._bolus) );
    }

    #region Messages
    private async Task AddTempTransform(Vector3D axis, float angle) {
        _rotation.AddTempRotation(axis, angle);
        _bolus.Transforms = _rotation;

        await BolusUpdated();
    }

    private async Task AddTransform(Vector3D axis, float angle) {
        //stack the transforms, save, and send update
        _rotation.ApplyRotation(axis, angle);
        _bolus.Transforms = _rotation;

        //testing new rotation
        _transform.AddRotation(axis, angle);
        _bolus.Transform = _transform;

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

        _bolus = new(mesh);
        _rotation = new();
        _bolus.Transforms = _rotation;
        _transform = new();
        _bolus.Transform = _transform;

        await BolusUpdated();
    }

    private async Task BolusUpdated() => WeakReferenceMessenger.Default.Send(new BolusUpdatedMessage(_bolus));

    private async Task ClearTransforms() {
        _rotation = new BolusRotation();
        _bolus.Transforms = _rotation;

        //testing new transforms
        _transform.ClearTransforms();
        _bolus.Transform = _transform;

        await BolusUpdated();
    }

    #endregion
}
