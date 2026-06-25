using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Fabolus.Wpf.Common.Mesh;
public static class MeshSkins {

    public static PointCollection GetTextureCoords(MeshGeometry3D mesh, Vector3D refAxis) {
        if (mesh == null) { return new PointCollection(); }

        var refAngle = 180.0f;
        var normals = mesh.Normals;

        PointCollection textureCoords = new PointCollection();
        foreach (var normal in normals) {
            double difference = Vector3D.AngleBetween(normal, refAxis);

            var ratio = difference / refAngle;

            textureCoords.Add(new Point(0, ratio));
        }

        return textureCoords;
    }

}
