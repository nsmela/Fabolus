using System.Collections.Generic;

namespace Fabolus.Wpf.Features.Main;

public record UpdateMeshInfoMessage(IEnumerable<MeshInfoItem> Items);
