
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Vector2Collection = HelixToolkit.Wpf.SharpDX.Vector2Collection;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;
using DiffuseMaterial = HelixToolkit.Wpf.SharpDX.DiffuseMaterial;
using HelixToolkit.Wpf.SharpDX;
using CommunityToolkit.Mvvm.Messaging;
using static Fabolus.Wpf.Stores.BolusStore;
using System.Security.Policy;
using HelixToolkit.Wpf.SharpDX.Model;


namespace Fabolus.Wpf.Common.Mesh;
public static class OverhangsHelper {
    private static Color4 BaseColor => new Color4(0, 0, 0, 1);
    private static Color4 WarningColor => new Color4(1, 1, 0, 1);
    private static Color4 FaultColor => new Color4(1, 0, 0, 1);


    public static HelixToolkit.Wpf.SharpDX.Material CreateOverhangsMaterial() {
        var colors = GetGradients(BaseColor, WarningColor, 60).ToList();
        foreach(var color in GetGradients(WarningColor, FaultColor, 15)) {
            colors.Add(color);
        }  
        foreach(var color in GetGradients(FaultColor, FaultColor, 25)) {
            colors.Add(color);
        }

        return new ColorStripeMaterial {
            ColorStripeX = colors,
            ColorStripeY = colors,
        };
    }

    public static Vector2Collection GetTextureCoordinates(MeshGeometry3D mesh, Vector3 refAxis) {
        if (mesh is null || mesh.Positions.Count() == 0) { return new Vector2Collection(); }

        var refAngle = 180.0;
        var normals = mesh.Normals;

        if (normals is null) { throw new NullReferenceException("Texture mesh normals are null"); }

        var textureCoords = new Vector2Collection();
        foreach (var normal in normals) {
            var difference = Math.Abs(normal.AngleBetween(refAxis)) * (180.0f / Math.PI);

            while (difference > refAngle) { difference -= refAngle; } //reduces angle until under 180 degrees

            float ratio = (float)(difference / refAngle);
            textureCoords.Add(new Vector2(0, ratio));
        }

        return textureCoords;
    }

    private static IEnumerable<Color4> GetGradients(Color4 start, Color4 mid, Color4 end, int steps) {
        return GetGradients(start, mid, steps / 2).Concat(GetGradients(mid, end, steps / 2));
    }

    private static IEnumerable<Color4> GetGradients(Color4 start, Color4 end, int steps) {
        float stepA = ((end.Alpha - start.Alpha) / (steps - 1));
        float stepR = ((end.Red - start.Red) / (steps - 1));
        float stepG = ((end.Green - start.Green) / (steps - 1));
        float stepB = ((end.Blue - start.Blue) / (steps - 1));

        for (int i = 0; i < steps; i++) {
            yield return new Color4((start.Red + (stepR * i)),
                                        (start.Green + (stepG * i)),
                                        (start.Blue + (stepB * i)),
                                        (start.Alpha + (stepA * i)));
        }
    }
}

