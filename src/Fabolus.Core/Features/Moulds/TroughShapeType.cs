namespace Fabolus.Core.Features.Moulds;

/// <summary>
/// Which part of the mould's top face the trough - the reservoir excess silicone pools in
/// while the mould fills - is recessed into.
/// </summary>
public enum TroughShapeType
{
    /// <summary>
    /// A basin across the whole top of the mould, inset from the outer wall by the trough
    /// margin so a rim is left to hold the silicone in.
    /// </summary>
    Footprint,

    /// <summary>
    /// A basin covering only where the air channels surface, spread out past them by the
    /// trough margin. Leaves the rest of the top face solid.
    /// </summary>
    Channels
}
