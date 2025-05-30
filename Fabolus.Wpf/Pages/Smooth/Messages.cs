using Fabolus.Core.Meshes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Pages.Smooth;

public sealed record SmoothingModelsUpdatedMessage(MeshModel[] GreenModels, MeshModel[] RedModels);
public sealed record SmoothingContourMessage(float z_height);
