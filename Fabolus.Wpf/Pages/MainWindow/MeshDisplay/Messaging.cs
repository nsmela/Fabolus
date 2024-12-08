using Fabolus.Wpf.Common.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
//messages
public sealed record MeshDisplayUpdatedMessasge(List<DisplayModel3D> models);
