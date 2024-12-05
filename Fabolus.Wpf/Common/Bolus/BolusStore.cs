using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Mesh;
using g3;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
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
    public sealed record ApplyRotationMessage(Vector3 axis, float angle);
    public sealed record ClearRotationsMessage();

    //request messages
    public class BolusRequestMessage : RequestMessage<BolusModel> { }

    #endregion

    #region Fields and Properties

    private Dictionary<string, BolusModel> _boli = []; //for different models used
    private BolusModel _bolus;
    private BolusTransform _transform = new();

    #endregion

    public BolusStore() {

        //registering messages
        WeakReferenceMessenger.Default.Register<AddBolusFromFileMessage>(this, async (r,m) => await BolusFromFile(m.filepath) );
        WeakReferenceMessenger.Default.Register<ApplyRotationMessage>(this, async (r, m) => await AddTransform(m.axis, m.angle));
        WeakReferenceMessenger.Default.Register<ClearRotationsMessage>(this, async (r, m) => await ClearTransforms());

        //request messages
        WeakReferenceMessenger.Default.Register<BolusStore, BolusRequestMessage>(this, (r, m) => m.Reply(r._bolus) );
    }

    #region Message Methods

    private async Task AddTransform(Vector3 axis, float angle) {
        _bolus.AddRotation(axis, angle);
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
        _transform = new();
        _bolus.Transform = _transform;

        await BolusUpdated();
    }

    private async Task BolusUpdated() => WeakReferenceMessenger.Default.Send(new BolusUpdatedMessage(_bolus));

    private async Task ClearTransforms() {
        //testing new transforms
        _transform = new();
        _bolus.ClearRotations();

        await BolusUpdated();
    }

    #endregion
}
