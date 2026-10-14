namespace Fabolus.Wpf.Common;

/// <summary>
/// Turns the engine's raw measurements into the units the panels present them in.
/// </summary>
/// <remarks>
/// The engine measures in whatever units the mesh is in, and every mesh here is in millimetres, so
/// a volume off it is mm3 and an area is mm2. Neither is what anyone wants to read: a bolus is a
/// hundred thousand cubic millimetres and a couple of hundred millilitres.
///
/// This lives in one place because the conversion was previously left to each panel, and two of
/// them simply printed the cubic millimetres with an "mL" suffix - every volume in the app was out
/// by a factor of a thousand, with the 107mL test bolus reading as 106882.57 mL.
/// </remarks>
public static class Measure
{
    private const double CubicMmPerMillilitre = 1000.0;
    private const double SquareMmPerSquareCentimetre = 100.0;

    /// <summary>Millilitres, from the cubic millimetres the engine reports.</summary>
    public static double ToMillilitres(double cubicMm) => cubicMm / CubicMmPerMillilitre;

    /// <summary>Square centimetres, from the square millimetres the engine reports.</summary>
    public static double ToSquareCentimetres(double squareMm) => squareMm / SquareMmPerSquareCentimetre;
}
