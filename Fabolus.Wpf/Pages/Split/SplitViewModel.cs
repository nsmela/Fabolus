using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Meshes;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.Mould;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Fabolus.Wpf.Pages.Split;

public partial class SplitViewModel : BaseViewModel {
    public override string TitleText => "split";

    public override SceneManager GetSceneManager => new SplitSceneManager();

    [ObservableProperty] private int _smoothnessDegree = 5;

    [RelayCommand]
    public async Task ExportSplits() {
        var models = WeakReferenceMessenger.Default.Send(new SplitRequestModels()).Response;
        if (models is null || models.Length == 0) { return; }

        SaveFileDialog saveFile = new() {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*"
        };

        //if successful, create mesh
        if (saveFile.ShowDialog() != true) { return; }

        var folder = Path.GetDirectoryName(saveFile.FileName);
        var filename = Path.GetFileNameWithoutExtension(saveFile.FileName);
        var filetype = Path.GetExtension(saveFile.FileName);

        string path = string.Empty;
        for (int i = 0; i < models.Length; i++) {
            path = Path.Combine(folder, $"{filename}0{i}{filetype}");
            await MeshModel.ToFile(path, models[i]);
        }

    }
}
