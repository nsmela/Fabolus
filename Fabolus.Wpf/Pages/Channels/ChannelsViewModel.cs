using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Scene;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Pages.Channels;

public class ChannelsViewModel : BaseViewModel {
    public override string TitleText => "Channels";

    public override SceneManager GetSceneManager => new ChannelsSceneManager();
}
