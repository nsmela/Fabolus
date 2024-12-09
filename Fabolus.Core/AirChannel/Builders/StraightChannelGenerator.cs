using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.AirChannel.Builders;
public static class StraightChannelGenerator {
    private const int SEGMENTS = 32;

    public static DMesh3 Build(Channel channel) {
        var mesh = new MeshEditor(new DMesh3());
        var radius = (float)(channel.Diameter / 2);
        var anchor = new Vector3d(
            channel.Origin.x,
            channel.Origin.y,
            channel.Origin.z - channel.Depth);

        //create cylinder
        var cyl_gen = new CappedCylinderGenerator {
            BaseRadius = radius,
            TopRadius = radius,
            Height = (float)(channel.Height - channel.Depth),
            Slices = SEGMENTS
        };

        cyl_gen.Generate();
        DMesh3 cylinder = new DMesh3(cyl_gen.MakeDMesh());
        var rotation = new Quaterniond(Vector3d.AxisX, 90.0f);
        MeshTransforms.Rotate(cylinder, Vector3d.Zero, rotation);
        MeshTransforms.Translate(cylinder, anchor);
        mesh.AppendMesh(cylinder);
        var result = mesh.Mesh;

        result.ReverseOrientation();//comes out with reversed normals

        return mesh.Mesh;
    }
}
