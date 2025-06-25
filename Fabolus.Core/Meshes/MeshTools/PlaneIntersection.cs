using Fabolus.Core.Extensions;
using static MR.DotNet;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {

    public static MeshModel PlaneIntersection(MeshModel model, double[] plane_origin, double[] plane_normal) {
        const float OFFSET = 1.0f;

        float[] min = new float[] {
            (float)model.Mesh.CachedBounds.Min.x + OFFSET,
            (float)model.Mesh.CachedBounds.Min.y + OFFSET,
            (float)model.Mesh.CachedBounds.Min.z,
        };

        float[] max = new float[] {
            (float)model.Mesh.CachedBounds.Max.x + OFFSET,
            (float)model.Mesh.CachedBounds.Max.y + OFFSET,
            (float)model.Mesh.CachedBounds.Max.z,
        };

        // create the plane mesh
        List<Vector3f> vertices = new() {
            new Vector3f(min[0], min[1], min[2]),
            new Vector3f(max[0], min[1], min[2]),
            new Vector3f(max[0], max[1], min[2]),
            new Vector3f(min[0], max[1], min[2]),
        };

        List<ThreeVertIds> ids = new() {
            new ThreeVertIds(0, 1, 2),
            new ThreeVertIds(0, 2, 3),
        };

        Mesh plane = Mesh.FromTriangles(vertices, ids);

        // transform the plane to the correct position and orientation
        Vector3f origin = new((float)plane_origin[0], (float)plane_origin[1], (float)plane_origin[2]);
        Vector3f direction = new((float)plane_normal[0], (float)plane_normal[1], (float)plane_normal[2]);

        Vector3f translate = new() { 
            X = 0, 
            Y = 0, 
            Z = origin.Z - min[2] 
        };

        plane.Transform(new AffineXf3f(translate));

        // TODO: rotate the plane to align with the normal

        // TODO: create mesh from intersection of plane and mesh
        var result = Boolean(model.Mesh.ToMesh(), plane, BooleanOperation.Intersection);

        return new MeshModel(result.mesh);
    }
}
