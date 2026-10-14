using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace Fabolus.Wpf.Features.Main;

public partial class InfoPanelViewModel : ObservableObject
{
    private readonly IMessenger _messenger;

    [ObservableProperty]
    private ObservableCollection<MeshInfoItem> _infoItems = new();

    public InfoPanelViewModel(IMessenger messenger)
    {
        _messenger = messenger;

        _messenger.Register<UpdateMeshInfoMessage>(this, (r, m) =>
        {
            InfoItems.Clear();
            foreach (var item in m.Items)
            {
                InfoItems.Add(item);
            }
        });
    }
}
