using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Fabolus.Wpf.Common.Bolus;
using g3;
using SharpDX;
using System.IO;

namespace Fabolus.Wpf.Bolus;
public class BolusStore {

    #region Messages
    //importing file
    public sealed record AddBolusFromFileMessage(string Filepath);
    public sealed record AddBolusMessage(string Label, BolusModel Bolus);

    //utility
    public sealed record BolusUpdatedMessage(BolusModel Bolus);
    public sealed record ClearBolusMessage(string Label);

    //rotations
    public sealed record ApplyRotationMessage(Vector3 Axis, float Angle);
    public sealed record ClearRotationsMessage();

    //request messages
    public class BolusRequestMessage : RequestMessage<BolusModel> { }

    #endregion

    #region Fields and Properties

    private Dictionary<string, BolusModel> _boli = []; //for different models used
    private BolusTransform _transform = new();

    private BolusModel LatestBolus() {
        if (_boli.ContainsKey(BolusModel.LABEL_MOULD)) { return _boli[BolusModel.LABEL_MOULD]; }
        if (_boli.ContainsKey(BolusModel.LABEL_SMOOTH)) { return _boli[BolusModel.LABEL_SMOOTH]; }
        if (_boli.ContainsKey(BolusModel.LABEL_RAW)) { return _boli[BolusModel.LABEL_RAW]; }

        return new BolusModel();
    }

    #endregion

    public BolusStore() {

        //registering messages
        WeakReferenceMessenger.Default.Register<AddBolusMessage>(this, async (r, m) => await AddBolus(m.Label, m.Bolus));
        WeakReferenceMessenger.Default.Register<AddBolusFromFileMessage>(this, async (r,m) => await BolusFromFile(m.Filepath));
        WeakReferenceMessenger.Default.Register<ApplyRotationMessage>(this, async (r, m) => await AddTransform(m.Axis, m.Angle));
        WeakReferenceMessenger.Default.Register<ClearBolusMessage>(this, async (r, m) => await ClearBolus(m.Label));
        WeakReferenceMessenger.Default.Register<ClearRotationsMessage>(this, async (r, m) => await ClearTransforms());

        //request messages
        WeakReferenceMessenger.Default.Register<BolusStore, BolusRequestMessage>(this, (r, m) => m.Reply(r.LatestBolus()));
    }

    #region Message Methods

    private async Task AddBolus(string label, BolusModel bolus) {
        _boli.Add(label, bolus);
        await BolusUpdated();
    }

    private async Task AddTransform(Vector3 axis, float angle) {
        _transform.AddRotation(axis, angle);
        await BolusUpdated();
    }

    private async Task BolusFromFile(string filepath) {
        _boli.Clear(); //a new imported mesh clears all exisiting meshes

        if (!File.Exists(filepath)) {
            System.Windows.MessageBox.Show("Unable to find: " + filepath);
            return;
        }

        var mesh = new DMesh3(await Task.Factory.StartNew(() => StandardMeshReader.ReadMesh(filepath)), false, true); //TODO: shouldn't be referencing DMesh3

        //if mesh isn't good
        if (mesh is null) {
            System.Windows.MessageBox.Show(filepath + " was an invalid mesh!");
            return;
        }

        _boli[BolusModel.LABEL_RAW] = new(mesh);
        _transform = new();

        await BolusUpdated();
    }

    private async Task BolusUpdated() {
        var bolus = LatestBolus();
        bolus.ApplyTransform(_transform);
        WeakReferenceMessenger.Default.Send(new BolusUpdatedMessage(bolus));
    }

    private async Task BolusUpdated(string label) {
        if (!_boli.ContainsKey(label)) { throw new Exception("Bolus Label " + label + "was not found!"); }

        WeakReferenceMessenger.Default.Send(new BolusUpdatedMessage(_boli[label]));
    }

    private async Task ClearBolus(string label) {
        if (_boli.ContainsKey(label)) { _boli.Remove(label); }
        await BolusUpdated();
    }

    private async Task ClearTransforms() {
        //testing new transforms
        _transform = new();

        await BolusUpdated();
    }

    #endregion
}
