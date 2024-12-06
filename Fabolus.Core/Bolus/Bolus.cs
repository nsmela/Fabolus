using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Bolus;
public class Bolus {
    public DMesh3 Mesh { get; set; }

    #region Constructors
    public Bolus() {
        Mesh = new DMesh3();
    }

    public Bolus(DMesh3 mesh) {
        Mesh = mesh;
    }

    #endregion

    #region Public Methods

    #endregion
}
