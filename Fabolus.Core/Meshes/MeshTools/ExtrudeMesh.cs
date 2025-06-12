using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.MeshTools;
public static partial class MeshTools {

    public static DMesh3 ExtrudeMesh(DMesh3 mesh, double distance) {
        MeshExtrudeMesh extruder = new(mesh){
            ExtrudedPositionF = (position, normal, index) => new Vector3d(position.x, position.y, position.z + distance),
        };
        extruder.Extrude();
        return extruder.Mesh;

    }


}
