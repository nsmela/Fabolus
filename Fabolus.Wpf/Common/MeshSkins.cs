using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using System.Windows.Media;

namespace Fabolus.Wpf.Common;
public static class MeshSkins {
    public record struct MeshSkin(string Label, Color Colour);

    public static MeshSkin Default = new MeshSkin("Default", Colors.WhiteSmoke);
    public static MeshSkin Bolus = new MeshSkin("Bolus", Colors.Gray);
    public static MeshSkin Smoothed = new MeshSkin("Smoothed", Colors.Gray);
    public static MeshSkin Warning = new MeshSkin("Warning", Colors.Gray);
    public static MeshSkin Fault = new MeshSkin("Fault", Colors.Gray);
    public static MeshSkin AirChannelTool = new MeshSkin("AirChannelTool", Colors.Gray);

    public enum MeshColor {
        Bolus,
        Smoothed,
        Warning,
        Fault,
        AirChannelTool,
        AirChannelSelected,
        AirChannel,
        MoldPreview,
        MoldFinal,
        MeshExport
    }

    public static GeometryModel3D SkinModel(MeshGeometry3D mesh, MeshSkin skin, double opacity = 1.0f) =>
        new GeometryModel3D {
            Geometry = mesh,
            Material = ColorToSkin(skin),
            BackMaterial = ColorToSkin(skin),
        };

    private static DiffuseMaterial ColorToSkin(MeshSkin skin, double opacity = 1.0f) =>
        new DiffuseMaterial(new SolidColorBrush { Color = skin.Colour, Opacity = opacity});

}
