using Fabolus.Core.BolusModel;
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
    
    public static Bolus SmoothBolus(Bolus bolus) {
        var smoother = new LaplacianMeshSmoother(bolus.Mesh);

        smoother.Initialize();
        //smoother.SetConstraint()

        smoother.SolveAndUpdateMesh();

        return new Bolus(smoother.Mesh);
    }
}
