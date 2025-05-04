using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Mould.Utils;

public class SignedDistanceContour {
    // ref: https://www.gradientspace.com/tutorials/2017/11/21/signed-distance-fields-tutorial

    public double CellSize { get; init; }
    public DMesh3 Mesh { get; set; }
    public int NumberOfCells { get; init; }
    public MeshSignedDistanceGrid SDF { get; set; }

    public SignedDistanceContour(DMesh3 mesh, int numberOfCells = 128) {
        Mesh = mesh;
        NumberOfCells = numberOfCells;
        CellSize = Mesh.CachedBounds.MaxDim / NumberOfCells;

        SDF = new MeshSignedDistanceGrid(Mesh, CellSize);
        SDF.Compute();
    }

    /// <summary>
    /// Using the SDF, the contour returns a Vector2 array representing the outline with an offset.
    /// </summary>
    /// <param name="offset"></param>
    /// <returns>How far from the original outline.</returns>
    public Vector2[] GetOutline(double offset) {
        //to process the mesh and generate the outline

        return [];
    }

}
