using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes.PartingTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static MR.DotNet;

namespace Fabolus.Core.Meshes.MeshTools;
public static partial class MeshTools {

    public static Result<CuttingIntersection[]> Intersections(MeshModel model_A, MeshModel model_B) {
        MeshPart meshA = new MeshPart(model_A.Mesh.ToMesh());
        MeshPart meshB = new MeshPart(model_B.Mesh.ToMesh());

        var converters = new CoordinateConverters(meshA, meshB);
        var intersections = FindCollidingEdgeTrisPrecise(meshA, meshB, converters);
        var ordered_intersections = IntersectionContour.OrderIntersectionContours(meshA.mesh, meshB.mesh, intersections);
        var contours = GetOneMeshIntersectionContours(meshA.mesh, meshB.mesh, ordered_intersections, true, converters);

        List<CuttingIntersection> results = [];
        foreach (var contour in contours) {
            results.Add(new CuttingIntersection { 
                IsClosed = contour.closed, 
                Points = contour
                    .intersections
                    .Select(i => i.coordinate.ToVector3()).ToArray() 
            });
        }

        return results.ToArray();
    }

}
