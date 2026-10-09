using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;

namespace Fabolus.Wpf.Features.About;

/// <summary>
/// What the About window shows: the version, the medical disclaimer, and where the licence
/// texts and documentation live.
/// </summary>
public partial class AboutViewModel
{
    public const string DocumentationUrl = "https://nsmela.github.io/Fabolus/";
    public const string RepositoryUrl = "https://github.com/nsmela/Fabolus";

    // DISCLAIMER.txt at the repository root is the one copy of the wording; the installer
    // shows the same file. Embedded rather than read from disk so the app can never start
    // without it.
    internal const string DisclaimerResourceName = "Fabolus.Disclaimer.txt";

    public string Version { get; } = ReadVersion();
    public string Disclaimer { get; } = LoadDisclaimer();

    [RelayCommand]
    private void OpenLicence() => OpenShippedFile("LICENSE");

    [RelayCommand]
    private void OpenThirdPartyNotices() => OpenShippedFile("THIRD-PARTY-NOTICES.md");

    [RelayCommand]
    private void OpenDocumentation() => OpenUrl(DocumentationUrl);

    [RelayCommand]
    private void OpenRepository() => OpenUrl(RepositoryUrl);

    /// <summary>
    /// The disclaimer as display text: the file is hard-wrapped for the installer, so the lines
    /// of each paragraph are joined back up and the window wraps them to its own width.
    /// </summary>
    internal static string LoadDisclaimer()
    {
        using var stream = typeof(AboutViewModel).Assembly.GetManifestResourceStream(DisclaimerResourceName)
            ?? throw new InvalidOperationException($"The '{DisclaimerResourceName}' resource is missing from the build.");
        using var reader = new StreamReader(stream);

        var paragraphs = reader.ReadToEnd()
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => string.Join(' ', p.Split('\n', StringSplitOptions.TrimEntries)));

        return string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
    }

    // The informational version carries the commit after a '+' (0.9.4+1a2b3c...). The number
    // is what people quote; the commit is shortened rather than dropped so a build can still
    // be traced back to its source.
    private static string ReadVersion()
    {
        var informational = typeof(AboutViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        }

        var parts = informational.Split('+', 2);
        return parts.Length == 2 && parts[1].Length >= 7
            ? $"{parts[0]} ({parts[1][..7]})"
            : parts[0];
    }

    // LICENSE and the notices ship beside the exe. Notepad rather than the shell's default
    // handler: LICENSE has no extension and .md often has no association, and either would
    // make the shell throw. A build run from somewhere the files were not copied to falls back
    // to the copy on GitHub rather than doing nothing.
    private static void OpenShippedFile(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path } });
        }
        else
        {
            OpenUrl($"{RepositoryUrl}/blob/main/{fileName}");
        }
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
