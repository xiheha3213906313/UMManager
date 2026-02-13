using UMManager.WinUI.Services.Notifications;
using Newtonsoft.Json;

namespace UMManager.WinUI.Models.Settings;

public class ModAttentionSettings
{
    [JsonIgnore] public const string Key = "ModAttentionSettings";

    public Dictionary<string, ModNotification[]> ModNotifications { get; set; } = new();
}