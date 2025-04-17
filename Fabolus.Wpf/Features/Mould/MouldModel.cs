using Fabolus.Core.Mould.Builders;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Extensions;
using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.Mould;
public sealed record MouldModel {
    public MeshGeometry3D Geometry { get; set; }

    public MouldModel() { }

    public MouldModel(SimpleMouldGenerator generator) {
        Geometry = generator.BuildPreview().ToGeometry();
    }
}
