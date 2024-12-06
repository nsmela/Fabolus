using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Smoothing;
public interface ISmoothingModel {
    public DMesh3 Smooth(DMesh3 mesh);
}
