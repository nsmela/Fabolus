using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes;
using Fabolus.Core.Mould.Builders;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Extensions;
using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Fabolus.Wpf.Features.Mould;
public sealed class MouldModel : MeshModel {
    public bool IsPreview { get; private set; }
    public MeshGeometry3D? Geometry { get; private set; }
    public string[] Errors { get; private set; } = [];

    public MouldModel() { }

    public MouldModel(MouldGenerator generator, bool isPreview = true) {
        IsPreview = isPreview;
        Result<MeshModel> result = IsPreview ? generator.Preview() : generator.Build();

        if (result.IsFailure) {
            Errors = result.Errors.Select(e => e.ErrorMessage).ToArray();
            MessageBox.Show(string.Join(Environment.NewLine, Errors), "Mould Generation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Mesh = result.Data;
        Geometry = result.Data.ToGeometry();
    }
}
