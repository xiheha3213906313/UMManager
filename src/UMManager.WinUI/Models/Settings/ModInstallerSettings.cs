using Newtonsoft.Json;

namespace UMManager.WinUI.Models.Settings;

internal class ModInstallerSettings
{
    [JsonIgnore] public const string Key = "ModInstallerSettings";
    public bool EnableModOnInstall { get; set; }
    public bool ModInstallerWindowOnTop { get; set; }
}