using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Fabolus.Core.Meshes;
using Fabolus.Wpf.Common.Bolus;
using SharpDX;
using System.IO;

namespace Fabolus.Wpf.Bolus;
public class BolusStore {

    #region Messages
    //importing file
    public sealed record AddBolusFromFileMessage(string Filepath);
    public sealed record AddBolusMessage(BolusModel Bolus, BolusType BolusType);

    //utility
    public sealed record BolusUpdatedMessage(BolusModel Bolus);
    public sealed record ClearBolusMessage(BolusType BolusType);

    //rotations
    public sealed record ApplyRotationMessage(Vector3 Axis, float Angle);
    public sealed record ClearRotationsMessage();

    //request messages
    public class BolusRequestMessage : RequestMessage<BolusModel> { }
    public class AllBolusRequestMessage : RequestMessage<BolusModel[]> { }

    #endregion

    #region Fields and Properties

    private Dictionary<BolusType, BolusModel> _boli = []; //for different models used
    private BolusTransform _transform = new();

    private BolusModel LatestBolus() {
        if (_boli.ContainsKey(BolusType.Mould)) { return _boli[BolusType.Mould]; }
        if (_boli.ContainsKey(BolusType.Smooth)) { return _boli[BolusType.Smooth]; }
        if (_boli.ContainsKey(BolusType.Raw)) { return _boli[BolusType.Raw]; }

        return new BolusModel();
    }

    #endregion

    public BolusStore() {

        //registering messages
        WeakReferenceMessenger.Default.Register<AddBolusMessage>(this, async (r, m) => await AddBolus(m.Bolus, m.BolusType));
        WeakReferenceMessenger.Default.Register<AddBolusFromFileMessage>(this, async (r,m) => await BolusFromFile(m.Filepath));
        WeakReferenceMessenger.Default.Register<ApplyRotationMessage>(this, async (r, m) => await AddTransform(m.Axis, m.Angle));
        WeakReferenceMessenger.Default.Register<ClearBolusMessage>(this, async (r, m) => await ClearBolus(m.BolusType));
        WeakReferenceMessenger.Default.Register<ClearRotationsMessage>(this, async (r, m) => await ClearTransforms());

        //request messages
        WeakReferenceMessenger.Default.Register<BolusStore, BolusRequestMessage>(this, (r, m) => m.Reply(r.LatestBolus()));
        WeakReferenceMessenger.Default.Register<BolusStore, AllBolusRequestMessage>(this, (r, m) => m.Reply(r._boli.Values.ToArray()));
    }

    #region Message Methods

    private async Task AddBolus(BolusModel bolus, BolusType type) {
        bolus.BolusType = type;
        _boli.Add(type, bolus);
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

        var mesh = await MeshModel.FromFile(filepath);

        //if mesh isn't good
        if (mesh is null) {
            System.Windows.MessageBox.Show(filepath + " was an invalid mesh!");
            return;
        }

        _boli[BolusType.Raw] = new(mesh);
        _transform = new();

        await BolusUpdated();
    }

    private async Task BolusUpdated() {
        var bolus = LatestBolus();
        bolus.ApplyTransform(_transform);
        WeakReferenceMessenger.Default.Send(new BolusUpdatedMessage(bolus));
    }

    private async Task BolusUpdated(BolusType type) {
        if (!_boli.ContainsKey(type)) { throw new Exception($"Bolus Label {type} was not found!"); }

        WeakReferenceMessenger.Default.Send(new BolusUpdatedMessage(_boli[type]));
    }

    private async Task ClearBolus(BolusType type) {
        if (_boli.ContainsKey(type)) { _boli.Remove(type); }
        await BolusUpdated();
    }

    private async Task ClearTransforms() {
        //testing new transforms
        _transform = new();

        await BolusUpdated();
    }

    #endregion
}
