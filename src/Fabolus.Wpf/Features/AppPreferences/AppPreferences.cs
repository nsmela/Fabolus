using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Fabolus.Wpf.Features.AppPreferences;

// ============================================================
//  MESSAGES
// ============================================================
public sealed class PreferenceSectionRequestMessage<T> : RequestMessage<T> where T : class, IPreferenceSettings { }

public sealed record PreferenceSectionUpdateMessage<T>(T Section) where T : class, IPreferenceSettings;

// ============================================================
//  STORE
// ============================================================

/// <summary>
/// Holds every preference and answers the section messages.
///
/// It knows nothing about individual preferences: each section reads and writes itself through
/// <see cref="IPreferenceSettings{TSelf}"/>, so this class does not grow when one is added.
/// </summary>
public sealed class AppPreferencesStore {
    private readonly IMessenger _messenger;
    private readonly PreferenceBag _bag;
    private readonly string _path;

    public AppPreferencesStore(IMessenger messenger)
        : this(messenger, PreferenceStorageLocation.DefaultPath) { }

    /// <param name="path">Where the preferences file lives. Overridden by tests.</param>
    public AppPreferencesStore(IMessenger messenger, string path) {
        _messenger = messenger;
        _path = path;

        var stored = ReadFile(_path);
        if (stored is not null) {
            _bag = stored;
        }
        else {
            // First run on this machine, or the file is gone. An older build kept preferences in
            // the exe config, so take those before falling back to the shipped defaults, and
            // write the result out so the migration only ever happens once.
            _bag = PreferenceBag.FromDefaults();
            LegacyExeConfig.CopyInto(_bag);
            Save();
        }

        PreferenceSections.ForEach(new Registrar(this));
    }

    /// <summary>The stored section, validated.</summary>
    public T Get<T>() where T : class, IPreferenceSettings<T> => T.Read(_bag).Clamped();

    /// <summary>Validates, stores and persists a section.</summary>
    public void Set<T>(T section) where T : class, IPreferenceSettings<T> {
        section.Clamped().Write(_bag);
        Save();
    }

    // ---- Message wiring -------------------------------------------------

    // A visitor rather than a loop: each section needs its type as a generic argument, and this
    // keeps registering all seven to one method instead of fourteen.
    private sealed class Registrar : IPreferenceSectionVisitor {
        private readonly AppPreferencesStore _store;
        public Registrar(AppPreferencesStore store) => _store = store;

        public void Visit<T>() where T : class, IPreferenceSettings<T> {
            _store._messenger.Register<AppPreferencesStore, PreferenceSectionRequestMessage<T>>(
                _store, (recipient, message) => message.Reply(recipient.Get<T>()));

            _store._messenger.Register<AppPreferencesStore, PreferenceSectionUpdateMessage<T>>(
                _store, (recipient, message) => recipient.Set(message.Section));
        }
    }

    // ---- Persistence ----------------------------------------------------

    private void Save() {
        try {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) { Directory.CreateDirectory(directory); }

            File.WriteAllText(_path, _bag.ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) {
            // A preference that cannot be written is not worth taking the app down for. The
            // value stays live for this session and the next write gets another go.
        }
    }

    private static PreferenceBag? ReadFile(string path) {
        try {
            if (!File.Exists(path)) { return null; }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object) { return null; }

            // Start from the defaults so a file missing a key still answers with something
            // sensible, then lay the stored values on top.
            var bag = PreferenceBag.FromDefaults();
            foreach (var property in document.RootElement.EnumerateObject()) {
                bag.SetRaw(property.Name, property.Value.Clone());
            }
            return bag;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or System.Security.SecurityException or JsonException) {
            // An unreadable or corrupt file falls back to defaults rather than refusing to start.
            return null;
        }
    }
}

/// <summary>Where preferences live on disk.</summary>
public static class PreferenceStorageLocation {
    public const string FileName = "preferences.json";
    public const string FolderName = "Fabolus";

    /// <summary>
    /// Per-user application data. The old exe-adjacent config could not be written at all when
    /// the app was installed somewhere read-only, and was shared between everyone on the machine.
    /// </summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName,
        FileName);
}

/// <summary>
/// Reads the UISettings section an older build wrote into the exe config, so upgrading does not
/// silently reset everything. The attribute names there are the storage keys the sections still
/// use, so this copies them across as text and lets each section parse and validate its own.
///
/// It reads the config as plain XML rather than through ConfigurationManager on purpose: the
/// configSections entry names a UISettings type that no longer exists, and asking the
/// configuration system for that section would fail trying to load it.
/// </summary>
internal static class LegacyExeConfig {
    private const string SectionName = "UISettings";

    public static void CopyInto(PreferenceBag bag) {
        try {
            var path = FindConfigFile();
            if (path is null || !File.Exists(path)) { return; }

            CopyFromXml(File.ReadAllText(path), bag);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or System.Security.SecurityException) {
            // Best-effort: an unreadable old config just means starting from defaults.
        }
    }

    /// <summary>Copies the section's attributes out of an exe config document.</summary>
    internal static void CopyFromXml(string xml, PreferenceBag bag) {
        XDocument document;
        try { document = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { return; }

        var section = document.Root?.Element(SectionName);
        if (section is null) { return; }

        foreach (var attribute in section.Attributes()) {
            if (attribute.IsNamespaceDeclaration) { continue; }
            bag.SetFromText(attribute.Name.LocalName, attribute.Value);
        }
    }

    private static string? FindConfigFile() {
        if (AppContext.GetData("APP_CONFIG_FILE") is string declared
            && !string.IsNullOrWhiteSpace(declared)
            && File.Exists(declared)) {
            return declared;
        }

        // Otherwise it sits beside the entry assembly. A .NET app writes <name>.dll.config;
        // check the .exe.config spelling too, since older builds produced that one.
        var name = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
        if (name is null) { return null; }

        foreach (var candidate in new[] { $"{name}.dll.config", $"{name}.exe.config" }) {
            var path = Path.Combine(AppContext.BaseDirectory, candidate);
            if (File.Exists(path)) { return path; }
        }

        return null;
    }
}
