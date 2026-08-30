using System;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features.AppPreferences;
using HelixToolkit.Wpf.SharpDX;

namespace Fabolus.Wpf.Features.Viewport;

/// <summary>
/// The print-bed grid drawn behind every scene, and its own subscription to the print-bed
/// preferences. Six scene managers each repeated the same read-three-values, re-read-all-three,
/// swap-the-visual block; they hold one of these instead.
/// </summary>
internal sealed class PrintBedGrid {
    private const float GridSpacing = 10f;

    private readonly IMessenger _messenger;
    private Element3D _grid;

    /// <summary>The grid as it stands now.</summary>
    public Element3D Current => _grid;

    /// <summary>
    /// Raised after the preferences change, with the id of the grid to drop and the one to add
    /// in its place.
    /// </summary>
    public event Action<Guid, Element3D>? Replaced;

    public PrintBedGrid(IMessenger messenger) {
        _messenger = messenger;
        _grid = Build(_messenger.GetSection(PrintBedPreferences.Default));

        _messenger.Register<PrintBedGrid, PreferenceSectionUpdateMessage<PrintBedPreferences>>(
            this, (recipient, message) => recipient.Rebuild(message.Section));
    }

    private void Rebuild(PrintBedPreferences bed) {
        var replacedId = _grid.GUID;
        _grid = Build(bed);
        Replaced?.Invoke(replacedId, _grid);
    }

    private static Element3D Build(PrintBedPreferences bed) =>
        SceneHelpers.GenerateGrid(bed.Width, bed.Depth, GridSpacing, bed.ShowGrid);
}
