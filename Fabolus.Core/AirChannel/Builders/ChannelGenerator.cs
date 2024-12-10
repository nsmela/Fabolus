using g3;
using HelixToolkit.Wpf.SharpDX.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Fabolus.Core.AirChannel.Builders.AngledChannelGenerator;

namespace Fabolus.Core.AirChannel.Builders;
public static class ChannelGenerator {
    public record Settings {
        public Settings() { }

        public ChannelTypes ChannelType { get; set; } = ChannelTypes.None;
        public float Diameter { get; set; } = 5.0f;
        public float Depth { get; set; } = 1.0f;
        public float Height { get; set; } = 20.0f;
        public Vector3d Origin { get; set; } = Vector3d.Zero;
    }

    public static Settings OfType(ChannelTypes type) => type switch {
        ChannelTypes.None => new Settings(),
        ChannelTypes.AngledHead => new AngledChannelGenerator.AngledSettings(),
        _ => throw new NotSupportedException($"ChannelType of {type} is not supported")
    };

    public static AngledSettings AngledChannel() =>
        new AngledChannelGenerator.AngledSettings();

    public static Settings SetDiameter(this Settings settings, double diameter) =>
        settings with { Diameter = (float)diameter };
    internal static AngledSettings SetOrigin(this AngledSettings settings, Vector3d origin) =>
    settings = settings with { Origin = origin };
    public static AngledSettings SetOrigin(this AngledSettings settings, double x, double y, double z) =>
        settings.SetOrigin(new Vector3d(x, y, z));
}
