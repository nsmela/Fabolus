using g3;
using SharpDX.DirectWrite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.AirChannel.Builders;
public static class AngledChannelGenerator {
    //ref: https://github.com/gradientspace/geometry3Sharp/blob/8f185f19a96966237ef631d97da567f380e10b6b/mesh_generators/GenCylGenerators.cs

    public static DMesh3 Build(Channel channel) {
        var mesh = new MeshEditor(new DMesh3());

        //create path
        var path = GeneratePath(channel.Origin);
        var curve = new DCurve3(path, false);
        var shape = Polygon2d.MakeCircle(channel.Diameter / 2, 32);

        //create tube
        var tube = new TubeGenerator(curve, shape);
        tube.Generate();
        mesh.AppendMesh(tube.MakeDMesh());

        return mesh.Mesh;
    }

    private static List<Vector3d> GeneratePath(Vector3d origin) {
        return new List<Vector3d> {
            origin,
            new Vector3d(origin.x + 10, origin.y + 10, origin.z + 10),
            new Vector3d(origin.x + 10, origin.y + 10, origin.z + 100)
        };
    }
}
