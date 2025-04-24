using Fabolus.Core.BolusModel;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Mould.Builders;

/// <summary>
/// The inherited class for all mould generators
/// </summary>
public abstract record MouldGenerator {
    protected DMesh3 BolusReference { get; set; } //mesh to invert while entirely within
    protected double OffsetXY { get; set; }
    protected double OffsetTop { get; set; } 
    protected double OffsetBottom { get; set; }
    protected double Resolution { get; set; }
    protected DMesh3[] ToolMeshes { get; set; } = []; // mesh to boolean subtract from the mold

    public abstract DMesh3 Build();

}
