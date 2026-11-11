using Microsoft.Windows.ApplicationModel.Resources;

namespace BtManage;

/// <summary>Localised strings from Strings/&lt;language&gt;/Resources.resw.</summary>
internal static class Strings
{
    private static readonly ResourceLoader Loader = new();

    public static string Get(string key) => Loader.GetString(key);

    public static string Format(string key, params object[] args) => string.Format(Get(key), args);
}
