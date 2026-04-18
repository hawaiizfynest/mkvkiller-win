using System;
using System.IO;
using System.Windows;

namespace MKVKiller;

public partial class App : Application
{
    public static string AppDataPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MKVKiller");

    protected override void OnStartup(StartupEventArgs e)
    {
        Directory.CreateDirectory(AppDataPath);
        Services.Preferences.Load();
        ApplyTheme(Services.Preferences.Current.Theme);
        base.OnStartup(e);
    }

    public static void ApplyTheme(string theme)
    {
        var dicts = Current.Resources.MergedDictionaries;
        // Remove any existing theme color dictionary (ThemeBlue, ThemeRed, ThemeGreen)
        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            var src = dicts[i].Source?.OriginalString ?? "";
            if (src.Contains("ThemeBlue") || src.Contains("ThemeRed") || src.Contains("ThemeGreen"))
            {
                dicts.RemoveAt(i);
            }
        }
        var themeFile = theme switch
        {
            "red" => "Themes/ThemeRed.xaml",
            "green" => "Themes/ThemeGreen.xaml",
            _ => "Themes/ThemeBlue.xaml"
        };
        dicts.Insert(1, new ResourceDictionary { Source = new Uri(themeFile, UriKind.Relative) });
        Services.Preferences.Current.Theme = theme;
        Services.Preferences.Save();
    }
}
