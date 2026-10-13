namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// Static pipeline-stage values for <see cref="IMeshCommand"/> types. Recording a command
/// clears any existing commands with a strictly greater priority, since they depended on
/// geometry this command just changed. Gaps are left deliberately so future commands can be
/// inserted without renumbering (e.g. an engraving feature might land between Transform and
/// Mould, or after Mould, depending on which geometry it targets).
/// </summary>
public static class CommandPriority {
    /// <summary>Rotate, Translate, Smoothing - siblings, none depends on the others.</summary>
    public const int Transform = 10;

    /// <summary>Text embossing / engraving on base or transformed mesh.</summary>
    public const int TextEmboss = 15;

    /// <summary>Depends on whatever geometry the Transform-stage commands produced.</summary>
    public const int Mould = 20;

    /// <summary>Text embossing / engraving on generated mould mesh.</summary>
    public const int MouldTextEmboss = 25;

    /// <summary>Splits a mould into pieces along a parting line. Depends on the Mould shape.</summary>
    public const int Split = 30;
}
