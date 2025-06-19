using Fabolus.Core.BolusModel;
using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static MR.DotNet;

namespace Fabolus.Core.Smoothing;
public static class LaplacianSmoothing {
    //ref: https://github.com/gradientspace/geometry3Sharp/blob/8f185f19a96966237ef631d97da567f380e10b6b/mesh_ops/LaplacianMeshSmoother.cs
    // https://www.cse.wustl.edu/~taoju/cse554/lectures/lect08_Deformation.pdf

    public static MeshModel Smooth(MeshModel model) {
        Mesh mesh = model.Mesh.ToMesh();

        var result = new Offset();

        return model;
    }
}
