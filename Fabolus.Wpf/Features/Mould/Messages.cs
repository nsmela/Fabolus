using CommunityToolkit.Mvvm.Messaging.Messages;
using Fabolus.Wpf.Features.Channels;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.Mould;

//updates
public sealed record MouldUpdatedMessage(MouldModel Mould);

//requests
public class MouldRequestMessage() : RequestMessage<MouldModel> { }
