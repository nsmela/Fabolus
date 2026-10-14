using Fabolus.Wpf.Features.CutSplit;
using Fabolus.Wpf.Features.Decal;
using Fabolus.Wpf.Features.Moulding;
using Fabolus.Wpf.Features.Rotatation;
using Fabolus.Wpf.Features.Smoothing;

namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// The roster of preference sections - the one list in the app that names them all.
///
/// A new section is added to <see cref="ForEach"/> and nowhere else: storage, restore-defaults,
/// export and import are all written against <see cref="IPreferenceSettings{TSelf}"/> and walk
/// that same list.
/// </summary>
internal static class PreferenceSections {

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

    /// <summary>Writes every section's shipped defaults into <paramref name="writer"/>.</summary>
    public static void WriteDefaults(IPreferenceWriter writer) => ForEach(new DefaultsWriter(writer));

    /// <summary>
    /// Reads every section through <paramref name="reader"/> and writes the validated result into
    /// <paramref name="writer"/>. Pass a <see cref="TrackingPreferenceReader"/> to find out what
    /// had to fall back to a default on the way through.
    /// </summary>
    public static void CopyValidated(IPreferenceReader reader, IPreferenceWriter writer) =>
        ForEach(new ValidatingCopier(reader, writer));

    private sealed class DefaultsWriter(IPreferenceWriter writer) : IPreferenceSectionVisitor {
        public void Visit<T>() where T : class, IPreferenceSettings<T> => T.Default.Write(writer);
    }

    private sealed class ValidatingCopier(IPreferenceReader reader, IPreferenceWriter writer)
        : IPreferenceSectionVisitor {
        public void Visit<T>() where T : class, IPreferenceSettings<T> =>
            T.Read(reader).Clamped().Write(writer);
    }
}

/// <summary>
/// Lets a caller run the same generic code for every section. A plain delegate cannot carry the
/// type argument each section needs, so the visit is an interface method instead.
/// </summary>
internal interface IPreferenceSectionVisitor {
    void Visit<T>() where T : class, IPreferenceSettings<T>;
}
