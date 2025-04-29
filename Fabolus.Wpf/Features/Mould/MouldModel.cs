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
public sealed record MouldModel {
    public MeshGeometry3D Geometry { get; set; }
    public string[] Errors { get; private set; } = [];

    public MouldModel() { }

    public MouldModel(SimpleMouldGenerator generator) {
        var result  = generator.Build();

        if (result.IsFailure) {
            Errors = result.Errors.Select(e => e.ErrorMessage).ToArray();
            MessageBox.Show(string.Join(Environment.NewLine, Errors), "Mould Generation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Geometry = result.Mesh.ToGeometry();
    }

    public static bool IsNullOrEmpty(MouldModel? mould) =>
        mould is null || 
        mould.Geometry is null || 
        mould.Geometry.TriangleIndices.Count() == 0;
}
