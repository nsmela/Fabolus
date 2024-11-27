using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Pages.Rotate;
public partial class RotateViewModel : BaseViewModel {
    public override string TitleText => "Rotation";

    public override BaseMeshViewModel GetMeshViewModel(BaseMeshViewModel? meshViewModel) => new RotateMeshViewModel(meshViewModel);
}
