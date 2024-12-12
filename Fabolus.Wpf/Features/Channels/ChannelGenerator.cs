using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.Channels;
public abstract record ChannelGenerator {
    protected const int SEGMENTS = 16;
    protected Vector3 Origin { get; set; } = Vector3.Zero; //where on model it starts
    protected Vector3 Normal { get; set; } = Vector3.UnitX; //direction of model's normal where origin is
    protected float Diameter { get; set; } = 5.0f;
    protected float Depth { get; set; } = 1.0f; //how deep within the model to start
    protected float MaxHeight { get; set; } = 100.0f; //how high the channel goes up to

    protected float Radius => Diameter / 2;

    public abstract MeshGeometry3D Build();
}