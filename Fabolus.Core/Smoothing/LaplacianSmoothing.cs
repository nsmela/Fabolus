using Fabolus.Core.BolusModel;
using Fabolus.Core.Extensions;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Smoothing;
public static class LaplacianSmoothing {
    //ref: https://github.com/gradientspace/geometry3Sharp/blob/8f185f19a96966237ef631d97da567f380e10b6b/mesh_ops/LaplacianMeshSmoother.cs
    // https://www.cse.wustl.edu/~taoju/cse554/lectures/lect08_Deformation.pdf

    public static Bolus SmoothBolus(Bolus bolus) {
        var mesh = (DMesh3)bolus.Mesh;
        //compute Laplacian coordinates
        var smoother = new LaplacianMeshSmoother(mesh);


        //measure local curvature
        //define an inflation weight
        //modify Laplacian target
        //solve deformation system
        smoother.Initialize();

        foreach(var i in Enumerable.Range(0, mesh.VertexCount)) {
            smoother.SetConstraint(i, mesh.GetVertex(i), 2.0, true);
        }

        smoother.SolveAndUpdateMesh();

        return new Bolus(smoother.Mesh);
    }
}
