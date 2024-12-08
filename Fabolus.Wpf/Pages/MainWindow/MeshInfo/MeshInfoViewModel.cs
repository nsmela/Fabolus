using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.MainWindow.MeshInfo;
public partial class MeshInfoViewModel : ObservableObject {
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private string _volumeText = string.Empty;

    public MeshInfoViewModel() {
        //messages
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => UpdateVolume());
        WeakReferenceMessenger.Default.Register<AddBolusFromFileMessage>(this, (r, m) => UpdateFilePath(m.Filepath));
    }

    private void UpdateFilePath(string filePath) {
        var path = Path.GetFileName(filePath);
        FileName = path;
    }

    private void UpdateVolume() {
        var boli = WeakReferenceMessenger.Default.Send(new AllBolusRequestMessage()).Response;

        var text = string.Empty;

        foreach (var bolus in boli) {
            var volume = bolus.Volume;
            text += $"[{bolus.BolusType}]: {string.Format("{0:0,0.0} mL", volume)}\r\n";
        }

        VolumeText = text;
    }
}
