using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Fabolus.Core.Bolus;
using g3;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System.IO;


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
    public sealed record ApplyRotationMessage(Vector3 axis, double angle);
    public sealed record ClearRotationsMessage();
    public sealed record ApplyOverhangSettingsMessage(double lower, double upper);

    //request messages
    public class BolusRequestMessage : RequestMessage<BolusModel> { }
    public class BolusFilePathRequestMessage : RequestMessage<string> { }
    public class BolusOverhangMaterialRequestMessage : RequestMessage<DiffuseMaterial> { }
    public class BolusOverhangSettingsRequestMessage : RequestMessage<double[]> { }

    #endregion

    #region Fields and Properties

    private BolusModel _bolus;

    #endregion

    public BolusStore()
    {
        //registering messages
        WeakReferenceMessenger.Default.Register<AddBolusFromFileMessage>(this, async (r,m) => await BolusFromFile(m.filepath) );

        //request messages
        WeakReferenceMessenger.Default.Register<BolusStore, BolusRequestMessage>(this, (r, m) => m.Reply(r._bolus) );
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

        await BolusUpdated();
    }

    private async Task BolusUpdated() => WeakReferenceMessenger.Default.Send(new BolusUpdatedMessage(_bolus));
}
