using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.Mould;
public class MouldStore {
    // variables to store
    MouldModel _mould = new();

    public MouldStore() {
        //listening
        WeakReferenceMessenger.Default.Register<MouldUpdatedMessage>(this, (r, m) => _mould = m.Mould);

        //requests
        WeakReferenceMessenger.Default.Register<MouldRequestMessage>(this, (r, m) => m.Reply(_mould));
    }
}
