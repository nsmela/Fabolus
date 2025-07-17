using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.AppPreferences;
// ref: https://www.youtube.com/watch?v=GWixs4RN10w
internal class UISettings : ConfigurationSection {

    public const string Label = "UISettings";
    public const string DefaultImportFolderLabel = "default_import_folder";
    public const string DefaultExportFolderLabel = "default_export_folder";
    public const string PrintBedWidthLabel = "print_bed_width";
    public const string PrintBedDepthLabel = "print_bed_depth";

    [ConfigurationProperty(DefaultImportFolderLabel)]
    public string DefaultImportFolder {
        get => (string)this[DefaultImportFolderLabel];
        set => this[DefaultImportFolderLabel] = value;
    }

    [ConfigurationProperty(DefaultExportFolderLabel)]
    public string DefaultExportFolder {
        get => (string)this[DefaultExportFolderLabel];
        set => this[DefaultExportFolderLabel] = value;
    }

    [ConfigurationProperty(PrintBedWidthLabel)]
    public float PrintBedWidth {
        get => (float)this[PrintBedWidthLabel];
        set => this[PrintBedWidthLabel] = value;
    }

    [ConfigurationProperty(PrintBedDepthLabel)]
    public float PrintBedDepth {
        get => (float)this[PrintBedDepthLabel];
        set => this[PrintBedDepthLabel] = value;
    }
}
