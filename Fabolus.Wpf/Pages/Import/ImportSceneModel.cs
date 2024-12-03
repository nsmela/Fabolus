using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Bolus;
using Fabolus.Core.Common;
using Fabolus.Wpf.Common.Scene;
using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using MeshHelper = Fabolus.Wpf.Common.Mesh.MeshHelper;
using static Fabolus.Wpf.Stores.BolusStore;
using SharpDX.Direct3D11;

namespace Fabolus.Wpf.Pages.Import;
public sealed class ImportSceneModel : SceneModel {
    private FillMode _fillMode = FillMode.Wireframe;
}
