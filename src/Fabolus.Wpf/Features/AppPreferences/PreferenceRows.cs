using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// One line in a preferences panel.
///
/// A section describes its rows; the view picks a template per row type. Values are reached
/// through delegates onto the preferences view model rather than by binding path, so a renamed
/// property is a compile error instead of a binding that silently stops working.
/// </summary>
public abstract class PreferenceRow : ObservableObject {
    /// <summary>Text on the left of the row. Group headers use it as their heading.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Optional second line under the label.</summary>
    public string? Caption { get; init; }

    private Func<bool>? _enabledWhen;

    /// <summary>
    /// Greys the row out when the predicate is false - a trough setting under a contoured
    /// mould, a decal default while the decal tool is off.
    /// </summary>
    public PreferenceRow EnabledWhen(Func<bool> predicate) {
        _enabledWhen = predicate;
        return this;
    }

    public bool IsEnabled => _enabledWhen?.Invoke() ?? true;

    /// <summary>Re-reads everything this row shows. Called when the view model changes underneath it.</summary>
    public virtual void Refresh() => OnPropertyChanged(nameof(IsEnabled));
}

/// <summary>A heading that introduces the rows beneath it.</summary>
public sealed class HeaderRow : PreferenceRow { }

/// <summary>A closing note. Carries no value.</summary>
public sealed class NoteRow : PreferenceRow { }

/// <summary>On/off switch.</summary>
public sealed class ToggleRow : PreferenceRow {
    public required Func<bool> Read { get; init; }
    public required Action<bool> Write { get; init; }

    public bool Value {
        get => Read();
        set { Write(value); OnPropertyChanged(); }
    }

    public override void Refresh() {
        base.Refresh();
        OnPropertyChanged(nameof(Value));
    }
}

/// <summary>A number with a spinner, and the unit it is measured in.</summary>
public sealed class NumberRow : PreferenceRow {
    public required Func<double> Read { get; init; }
    public required Action<double> Write { get; init; }

    /// <summary>Shown next to the label, e.g. "mm". The axis rows use "X", "Y".</summary>
    public string? Unit { get; init; }

    /// <summary>Resource key for the unit's colour, so the print bed keeps its axis colours.</summary>
    public string UnitBrushKey { get; init; } = "Brush.Text.Muted";

    public double Minimum { get; init; }
    public double Maximum { get; init; } = double.MaxValue;
    public double Interval { get; init; } = 1;
    public string StringFormat { get; init; } = "N1";

    public double Value {
        get => Read();
        set { Write(value); OnPropertyChanged(); }
    }

    public override void Refresh() {
        base.Refresh();
        OnPropertyChanged(nameof(Value));
    }
}

/// <summary>One entry in a choice row.</summary>
public sealed record PreferenceChoice(object Value, string Label);

/// <summary>Shared shape of the two pickers.</summary>
public abstract class ChoiceRow : PreferenceRow {
    public required IReadOnlyList<PreferenceChoice> Choices { get; init; }
    public required Func<object> Read { get; init; }
    public required Action<object> Write { get; init; }

    public object Value {
        get => Read();
        set { if (value is not null) { Write(value); OnPropertyChanged(); } }
    }

    public override void Refresh() {
        base.Refresh();
        OnPropertyChanged(nameof(Value));
    }
}

/// <summary>A short choice shown as a segmented control.</summary>
public sealed class SegmentedRow : ChoiceRow { }

/// <summary>A longer choice shown as a drop-down.</summary>
public sealed class DropdownRow : ChoiceRow {
    public double PickerWidth { get; init; } = 150;
}

/// <summary>
/// A drop-down paired with its own on/off switch - the decal auto-placement rows, where the
/// anchor only means anything while that placement is switched on.
/// </summary>
public sealed class AnchoredToggleRow : PreferenceRow {
    public required IReadOnlyList<PreferenceChoice> Choices { get; init; }
    public required Func<object> ReadAnchor { get; init; }
    public required Action<object> WriteAnchor { get; init; }
    public required Func<bool> ReadEnabled { get; init; }
    public required Action<bool> WriteEnabled { get; init; }

    public object Anchor {
        get => ReadAnchor();
        set { if (value is not null) { WriteAnchor(value); OnPropertyChanged(); } }
    }

    public bool Value {
        get => ReadEnabled();
        set { WriteEnabled(value); OnPropertyChanged(); OnPropertyChanged(nameof(Value)); }
    }

    public override void Refresh() {
        base.Refresh();
        OnPropertyChanged(nameof(Anchor));
        OnPropertyChanged(nameof(Value));
    }
}

/// <summary>A folder path with a browse button.</summary>
public sealed class FolderRow : PreferenceRow {
    public required Func<string> Read { get; init; }
    public required ICommand Browse { get; init; }

    public string Value => Read();

    public override void Refresh() {
        base.Refresh();
        OnPropertyChanged(nameof(Value));
    }
}

/// <summary>
/// A row the descriptors cannot express, rendered by a DataTemplate the view supplies under
/// <see cref="TemplateKey"/>. The escape hatch that keeps the other row types from growing a
/// special case every time one panel needs something of its own.
/// </summary>
public sealed class CustomRow : PreferenceRow {
    public required string TemplateKey { get; init; }

    /// <summary>The preferences view model, so a bespoke template can bind straight to it.</summary>
    public required object Context { get; init; }
}
