using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace LanDrop.Desktop;

internal static class DesktopTheme
{
    internal static string Normalize(string? preference) => preference is "light" or "dark" ? preference : "system";

    internal static bool SystemIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return true; }
    }

    internal static void Apply(ResourceDictionary resources, string? preference, bool? highContrast = null, bool? systemLight = null)
    {
        if (highContrast ?? SystemParameters.HighContrast)
        {
            resources["WindowBrush"] = SystemColors.WindowBrush;
            resources["TextBrush"] = resources["MutedBrush"] = resources["WarningBrush"] = resources["AccentBrush"] = SystemColors.WindowTextBrush;
            resources["BorderBrush"] = resources["FocusBrush"] = SystemColors.WindowTextBrush;
            resources["ButtonBrush"] = SystemColors.ControlBrush;
            resources["ButtonTextBrush"] = SystemColors.ControlTextBrush;
            resources["ButtonHoverBrush"] = SystemColors.HighlightBrush;
            resources["ButtonHoverTextBrush"] = SystemColors.HighlightTextBrush;
            resources["DisabledTextBrush"] = SystemColors.GrayTextBrush;
            return;
        }
        var light = Normalize(preference) == "light" || Normalize(preference) == "system" && (systemLight ?? SystemIsLight());
        void Set(string key, string value) => resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        Set("WindowBrush", light ? "#F6F8F3" : "#101416");
        Set("TextBrush", light ? "#18201A" : "#F3F5F2");
        Set("MutedBrush", light ? "#49574F" : "#B5C0BB");
        Set("WarningBrush", light ? "#735100" : "#E2C879");
        Set("AccentBrush", light ? "#365B09" : "#C2F970");
        Set("BorderBrush", light ? "#627267" : "#7B8F84");
        Set("FocusBrush", light ? "#365B09" : "#C2F970");
        Set("ButtonBrush", light ? "#365B09" : "#C2F970");
        Set("ButtonTextBrush", light ? "#FFFFFF" : "#162208");
        Set("ButtonHoverBrush", light ? "#274406" : "#D3FFA0");
        Set("ButtonHoverTextBrush", light ? "#FFFFFF" : "#162208");
        Set("DisabledTextBrush", light ? "#49574F" : "#B5C0BB");
    }
}
