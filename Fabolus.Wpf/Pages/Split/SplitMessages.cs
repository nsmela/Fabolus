using CommunityToolkit.Mvvm.Messaging.Messages;
using Fabolus.Core.Meshes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Pages.Split;

public sealed class SplitRequestModels : RequestMessage<MeshModel[]>;
