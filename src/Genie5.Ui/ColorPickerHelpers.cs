using System;
using Avalonia.Controls;
using Avalonia.Media;

namespace Genie5.Ui;

// Shared helpers for binding Avalonia ColorPicker + a "Default/None" CheckBox
// to the string-based color values used across the preset/highlight/layout
// configuration (e.g. "Default", "", "IndianRed", "#FF8800").
internal static class ColorPickerHelpers
{
    // Seeds the picker & checkbox from a stored color string.
    public static void LoadColor(ColorPicker picker, CheckBox sentinel,
                                 string value, string sentinelKeyword)
    {
        if (IsSentinel(value, sentinelKeyword))
        {
            sentinel.IsChecked = true;
            return;
        }

        sentinel.IsChecked = false;
        if (Color.TryParse(value, out var c))
            picker.Color = c;
    }

    // Returns the string value to persist back to storage.
    // When the sentinel checkbox is set, returns the sentinel keyword (e.g.
    // "Default" for foregrounds, "" for backgrounds). Otherwise "#RRGGBB".
    public static string ReadColor(ColorPicker picker, CheckBox sentinel, string sentinelKeyword)
    {
        if (sentinel.IsChecked == true) return sentinelKeyword;
        var c = picker.Color;
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static bool IsSentinel(string value, string sentinelKeyword)
    {
        if (string.IsNullOrEmpty(value)) return true;
        if (!string.IsNullOrEmpty(sentinelKeyword)
            && value.Equals(sentinelKeyword, StringComparison.OrdinalIgnoreCase))
            return true;
        return value.Equals("(none)", StringComparison.OrdinalIgnoreCase);
    }
}
