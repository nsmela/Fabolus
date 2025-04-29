using CommunityToolkit.Mvvm.Messaging.Messages;
using Fabolus.Core.Mould.Builders;
using Fabolus.Wpf.Features.Channels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.Mould;

//updates
public sealed record MouldUpdatedMessage(MouldModel Mould);
public sealed record MouldGeneratorUpdatedMessage(MouldGenerator Generator);

//requests
public class MouldRequestMessage() : RequestMessage<MouldModel> { }
public class MouldGeneratorRequest() : RequestMessage<MouldGenerator> { }
