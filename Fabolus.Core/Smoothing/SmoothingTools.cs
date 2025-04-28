using Fabolus.Core.BolusModel;
using Fabolus.Core.Extensions;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Smoothing;
public static class SmoothingTools {
    //References:
    //https://www.gradientspace.com/tutorials/2018/9/14/point-set-fast-winding

    //get texture coordinates
    public static float[] GenerateTextureCoordinates(Bolus newBolus, Bolus oldBolus) {
        var lower = -2.0f;
        var upper = 2.0f;
        var spread = upper - lower;
        var mesh = oldBolus.Mesh;
        var spatial = new DMeshAABBTree3(mesh); //TODO: move to store the spatial on Bolus to improve speed
        spatial.Build();

        var values = new List<float>();
        foreach(var v in newBolus.Mesh.Vectors()) { //TODO: convert to parallel, but account for safesetting and race conditions
            var point = v.ToVector3d();
            var distance = DistanceToMesh(point, mesh, spatial);
            values.Add(DistanceToRatio(lower, upper, spread, spatial.IsInside(point), distance));
        }
        return values.ToArray();
    }

    private static float DistanceToMesh(Vector3d point, DMesh3 mesh, DMeshAABBTree3 spatial) =>
        (float)MeshQueries.NearestPointDistance(mesh, spatial, point, 10.0);

    private static float DistanceToRatio(float lower, float upper, float spread, bool isInside, float distance) {
        var value = isInside
            ? Math.Max(distance * -1, lower) //make negative if within the mesh and test against lowest value
            : Math.Min(distance, upper); //test against highest value
        value -= lower; //converting from distance to value on the spread by subtracting lower to make lowest possible value equal 0
        
        return value / spread; //convert to a ratio
    }

}
