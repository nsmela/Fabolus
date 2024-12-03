using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using Color = SharpDX.Color;

namespace Fabolus.Wpf.Common.Mesh;
public static class MeshSkins {

    public static Dictionary<string, Color> SkinColors = new() {
        { "Default", Color.WhiteSmoke},
        { "Bolus", Color.Gray },
        { "Smoothed", Color.Gray },
        { "Warning", Color.Gray },
        { "Fault", Color.Gray },
        { "AirChannelTool", Color.Gray },
        { "AirChannelSelected", Color.Gray },
        { "AirChannel",Color.Gray },
        { "MoldPreview",Color.Gray },
        { "MoldFinal",Color.Gray },
        { "MeshExport", Color.Gray },
    };

    public static string[] MeshColors => SkinColors.Keys.ToArray();
    public static Color GetColor(string skinName) =>
        SkinColors.Keys.Contains(skinName)
        ? SkinColors[skinName]
        : SkinColors["Default"];

}
