using Newtonsoft.Json;

namespace UMManager.WinUI.Models.Options;

public class UpdateCheckerOptions
{
    [JsonIgnore] public const string Key = "UpdateChecker";
    public Version? IgnoreNewVersion { get; set; }
}