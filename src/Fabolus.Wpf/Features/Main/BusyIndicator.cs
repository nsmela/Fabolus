using CommunityToolkit.Mvvm.Messaging;

namespace Fabolus.Wpf.Features.Main;

/// <summary>
/// Raises the viewport's loading overlay for as long as a scope is held.
/// </summary>
/// <remarks>
/// <para>
/// The overlay is one shared flag, so overlapping operations are counted rather than each
/// lowering it on the way out: an inner scope closing must not clear the overlay while an outer
/// one is still working. Only ever used from the UI thread, so a plain counter is enough.
/// </para>
/// <para>
/// Raising this around work that runs on the UI thread shows the user nothing - the overlay
/// cannot paint while the thread that would paint it is busy - so a caller that takes a scope
/// also pushes the slow part off that thread. The flag on its own is decoration.
/// </para>
/// </remarks>
public sealed class BusyIndicator(IMessenger messenger)
{
    private readonly IMessenger _messenger = messenger;
    private int _depth;

    public Scope Enter() => new(this);

    public readonly struct Scope : IDisposable
    {
        private readonly BusyIndicator _owner;

        internal Scope(BusyIndicator owner)
        {
            _owner = owner;
            if (owner._depth++ == 0)
            {
                owner._messenger.Send(new IsLoadingMessage(true));
            }
        }

        public void Dispose()
        {
            if (--_owner._depth == 0)
            {
                _owner._messenger.Send(new IsLoadingMessage(false));
            }
        }
    }
}
