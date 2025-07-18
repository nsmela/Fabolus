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

    [ObservableProperty] private float _seperationDistance = 0.3f;

    partial void OnSeperationDistanceChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        WeakReferenceMessenger.Default.Send(new SplitSeperationDistanceMessage(newValue));
    }

    [RelayCommand]
    private async Task Generate() {

    }

    [RelayCommand]
    private async Task ExportSeperate() {
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

    [RelayCommand]
    private async Task ExportJoined() {
        var models = WeakReferenceMessenger.Default.Send(new SplitRequestModels()).Response;
        if (models is null || models.Length == 0) { return; }

        SaveFileDialog saveFile = new() {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*"
        };

        //if successful, create mesh
        if (saveFile.ShowDialog() != true) { return; }

        // saving both models in a single STL file with a small gap between them
        // copying the meshes to ensure they dont modify the originals
        MeshModel negative_parting_model = MeshModel.Copy(models[0]);
        MeshModel positive_parting_model = MeshModel.Copy(models[1]);
        positive_parting_model.ApplyTranslation(0, SeperationDistance, 0); // move to create gap

        MeshModel combinedModel = MeshModel.Combine([
            negative_parting_model,
            positive_parting_model,
        ]);

        await MeshModel.ToFile(saveFile.FileName, combinedModel);
    }
}
