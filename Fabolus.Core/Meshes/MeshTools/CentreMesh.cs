using Fabolus.Core.Extensions;
using g3;
using gs;
using static MR.DotNet;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools
{

    public static MeshModel OrientationCentre(MeshModel model)
    {
        float x = model._mesh.BoundingBox.Center().X;
        float y = model._mesh.BoundingBox.Center().Y;
        float z = model._mesh.BoundingBox.Center().Z;

        MeshTransforms.Translate(model.Mesh, -new Vector3d(x, y, z));
        model._mesh = model.Mesh.ToMesh();

        return model;
    }
}
