using Fabolus.Core.Meshes;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static MR.DotNet;

namespace Fabolus.Core.BolusModel;

public class Bolus {

    #region Public Properties and Fields

    public MeshModel Mesh { get; set; }

    public double Volume => Mesh.Volume;

    #endregion

    #region Constructors
    public Bolus() {
        Mesh = new MeshModel();
    }

    public Bolus(MeshModel mesh) {
        Mesh = mesh;
    }

    #endregion

}
