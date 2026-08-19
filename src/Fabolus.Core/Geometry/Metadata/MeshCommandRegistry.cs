using Fabolus.Core.Common;

namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// Resolves the command names stored in save files back to <see cref="IMeshCommand"/> types.
/// Save files outlive the code that wrote them, so the stored name is a persistence contract,
/// not just a class name: renaming a command type without recording the old name here makes
/// every existing file's history silently disagree with its baked geometry.
/// </summary>
public static class MeshCommandRegistry {
    /// <summary>
    /// Names written by earlier versions that no longer match a live type name, mapped to the
    /// name in use today. Add an entry here whenever an <see cref="IMeshCommand"/> is renamed.
    /// </summary>
    private static readonly Dictionary<string, string> LegacyNames = new(StringComparer.OrdinalIgnoreCase) {
        ["SmoothCommand"] = "SmoothSettings",
    };

    private static readonly Lazy<IReadOnlyDictionary<string, Type>> CommandTypes = new(() =>
        typeof(IMeshCommand).Assembly.GetTypes()
            .Where(t => typeof(IMeshCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The name to persist for <paramref name="command"/>. Current type names are the canonical
    /// form; <see cref="LegacyNames"/> only ever maps inbound.
    /// </summary>
    public static string GetName(IMeshCommand command) => command.GetType().Name;

    /// <summary>
    /// Resolves a stored command name, falling back to the legacy alias table. Fails rather
    /// than returning nothing, so an unrecognised command surfaces at import instead of
    /// vanishing from the mesh's history.
    /// </summary>
    public static Result<Type> ResolveType(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return MetadataErrors.UnknownCommand(name ?? string.Empty);
        }

        if (CommandTypes.Value.TryGetValue(name, out var type)) {
            return Result<Type>.Success(type);
        }

        if (LegacyNames.TryGetValue(name, out var currentName)
            && CommandTypes.Value.TryGetValue(currentName, out var aliased)) {
            return Result<Type>.Success(aliased);
        }

        return MetadataErrors.UnknownCommand(name);
    }
}
