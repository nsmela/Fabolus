using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Mould.Builders;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Features.Channels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Features.Mould;
public class MouldStore {
    // variables to store
    MouldModel _mould = new();
    MouldGenerator _generator;

    public MouldStore() {
        //listening
        WeakReferenceMessenger.Default.Register<MouldUpdatedMessage>(this, (r, m) => _mould = m.Mould);
        WeakReferenceMessenger.Default.Register<MouldGeneratorUpdatedMessage>(this, (r,m) => UpdateGenerator(m.Generator));
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => _mould = new());
        WeakReferenceMessenger.Default.Register<AirChannelsUpdatedMessage>(this, (r, m) => _mould = new());

        //requests
        WeakReferenceMessenger.Default.Register<MouldRequestMessage>(this, (r, m) => m.Reply(_mould));
        WeakReferenceMessenger.Default.Register<MouldGeneratorRequest>(this, (r, m) => m.Reply(_generator));
    }

    private void UpdateGenerator(MouldGenerator generator) {
        _generator = generator;
        _mould = new MouldModel(generator);

        WeakReferenceMessenger.Default.Send(new MouldUpdatedMessage(_mould));
    }
}
