using Fabolus.Wpf.Features.CutSplit;
using Fabolus.Wpf.Features.Decal;
using Fabolus.Wpf.Features.Moulding;
using Fabolus.Wpf.Features.Rotatation;
using Fabolus.Wpf.Features.Smoothing;

namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// The roster of preference sections - the one list in the app that names them all.
///
/// A new section is added here and nowhere else: storage, restore-defaults, export and import
/// are all written against <see cref="IPreferenceSettings{TSelf}"/> and pick it up from this list.
/// </summary>
internal static class PreferenceSections {

    /// <summary>Writes every section's shipped defaults into <paramref name="writer"/>.</summary>
    public static void WriteDefaults(IPreferenceWriter writer) {
        GeneralPreferences.Default.Write(writer);
        PrintBedPreferences.Default.Write(writer);
        CutSplitPreferences.Default.Write(writer);
        DecalPreferences.Default.Write(writer);
        SmoothingPreferences.Default.Write(writer);
        RotationPreferences.Default.Write(writer);
        MouldPreferences.Default.Write(writer);
    }

    /// <summary>
    /// Reads every section through <paramref name="reader"/> and writes the validated result into
    /// <paramref name="writer"/>. Pass a <see cref="TrackingPreferenceReader"/> to find out what
    /// had to fall back to a default on the way through.
    /// </summary>
    public static void CopyValidated(IPreferenceReader reader, IPreferenceWriter writer) {
        GeneralPreferences.Read(reader).Clamped().Write(writer);
        PrintBedPreferences.Read(reader).Clamped().Write(writer);
        CutSplitPreferences.Read(reader).Clamped().Write(writer);
        DecalPreferences.Read(reader).Clamped().Write(writer);
        SmoothingPreferences.Read(reader).Clamped().Write(writer);
        RotationPreferences.Read(reader).Clamped().Write(writer);
        MouldPreferences.Read(reader).Clamped().Write(writer);
    }

    /// <summary>Hands each section type to <paramref name="register"/>, once.</summary>
    public static void ForEach(IPreferenceSectionVisitor register) {
        register.Visit<GeneralPreferences>();
        register.Visit<PrintBedPreferences>();
        register.Visit<CutSplitPreferences>();
        register.Visit<DecalPreferences>();
        register.Visit<SmoothingPreferences>();
        register.Visit<RotationPreferences>();
        register.Visit<MouldPreferences>();
    }
}

/// <summary>
/// Lets a caller run the same generic code for every section. A plain delegate cannot carry the
/// type argument each section needs, so the visit is an interface method instead.
/// </summary>
internal interface IPreferenceSectionVisitor {
    void Visit<T>() where T : class, IPreferenceSettings<T>;
}
