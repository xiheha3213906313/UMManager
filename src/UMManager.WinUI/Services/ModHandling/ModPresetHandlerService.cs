using UMManager.Core.Helpers;
using UMManager.Core.Contracts.Services;
using UMManager.Core.Services;
using UMManager.Core.Services.ModPresetService;
using UMManager.Core.Services.ModPresetService.Models;
using UMManager.WinUI.Contracts.Services;
using UMManager.WinUI.Models.Settings;
using UMManager.WinUI.Services.Notifications;
using Serilog;

namespace UMManager.WinUI.Services.ModHandling;

public sealed class ModPresetHandlerService(
    ILogger logger,
    ModPresetService modPresetService,
    UserPreferencesService preferencesService,
    NotificationManager notificationManager,
    ElevatorService elevatorService,
    ILocalSettingsService localSettingsService)
{
    private readonly ILogger _logger = logger.ForContext<ModPresetHandlerService>();
    private readonly ModPresetService _modPresetService = modPresetService;
    private readonly UserPreferencesService _userPreferencesService = preferencesService;
    private readonly NotificationManager _notificationManager = notificationManager;
    private readonly ElevatorService _elevatorService = elevatorService;
    private readonly ILocalSettingsService _localSettingsService = localSettingsService;


    public Task<IEnumerable<ModPreset>> GetModPresetsAsync()
        => Task.FromResult(_modPresetService.GetPresets().OrderBy(p => p.Index).AsEnumerable());

    public async Task<Result> ApplyModPresetAsync(string presetName, CancellationToken cancellationToken = default)
    {
        try
        {
            return await InternalModPresetAsync(presetName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
#if DEBUG
            throw;
#endif

            _logger.Error(e, "An error occured when applying preset {PresetName}", presetName);
            var localizer = App.GetService<ILanguageLocalizer>();
            return Result.Error(new SimpleNotification(
                localizer.GetLocalizedStringOrDefault("Notification.ApplyPresetFailed.Title", defaultValue: "应用预设失败"),
                e.Message,
                null));
        }
    }


    private async Task<Result> InternalModPresetAsync(string presetName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetName, nameof(presetName));
        await _modPresetService.ApplyPresetAsync(presetName, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var preferencesResult = await _userPreferencesService
            .SetModPreferencesAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!preferencesResult)
        {
            var localizer = App.GetService<ILanguageLocalizer>();
            _notificationManager.ShowNotification(
                localizer.GetLocalizedStringOrDefault("Notification.WritePreferencesFailed.Title",
                    defaultValue: "无法将模组偏好写入 3Dmigoto 的 user.ini"),
                localizer.GetLocalizedStringOrDefault("Notification.SeeLogs.Message", defaultValue: "详情请查看日志。"),
                null);
        }


        var modPreset = _modPresetService.GetPreset(presetName);


        var presetAppliedLocalizer = App.GetService<ILanguageLocalizer>();
        var simpleNotification = new SimpleNotification(
            presetAppliedLocalizer.GetLocalizedStringOrDefault("Notification.PresetApplied.Title", defaultValue: "预设已应用"),
            string.Format(presetAppliedLocalizer.GetLocalizedStringOrDefault("Notification.PresetApplied.Message",
                    defaultValue: "预设“{0}”已应用")!,
                modPreset.Name),
            TimeSpan.FromSeconds(5));


        if (await CanAutoSyncAsync().ConfigureAwait(false))
        {
            await _elevatorService.RefreshGenshinMods().ConfigureAwait(false);
            if (modPreset.Mods.Count == 0)
                return Result.Success(simpleNotification);

            await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
            await _userPreferencesService.SetModPreferencesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }


        if (await CanAutoSyncAsync().ConfigureAwait(false))
        {
            //await ElevatorService.RefreshGenshinMods().ConfigureAwait(false); // Wait and check for changes timout 5 seconds
            //await Task.Delay(5000).ConfigureAwait(false);
            await _elevatorService.RefreshAndWaitForUserIniChangesAsync().ConfigureAwait(false);
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            await _userPreferencesService.SetModPreferencesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }


        if (await CanAutoSyncAsync().ConfigureAwait(false))
        {
            await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
            await _elevatorService.RefreshGenshinMods().ConfigureAwait(false);
        }


        return Result.Success(simpleNotification);
    }


    private async Task<bool> CanAutoSyncAsync()
    {
        var autoSync = await _localSettingsService.ReadOrCreateSettingAsync<ModPresetSettings>(ModPresetSettings.Key)
            .ConfigureAwait(false);

        return _elevatorService.CheckStatus() == ElevatorStatus.Running && autoSync.AutoSyncMods;
    }

    public async Task<Result> SaveActiveModPreferencesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await InternalSaveActivePreferencesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
#if DEBUG
            throw;
#endif

            _logger.Error(e, "An error occured when saving active preferences");
            return Result.Error(new SimpleNotification("Failed to save active preferences", e.Message, null));
        }
    }

    private async Task<Result> InternalSaveActivePreferencesAsync(CancellationToken cancellationToken)
    {
        await _userPreferencesService.SaveModPreferencesAsync().ConfigureAwait(false);

        return Result.Success(new SimpleNotification("Active preferences saved",
            $"Preferences stored in {Constants.UserIniFileName} have been saved for enabled mods",
            TimeSpan.FromSeconds(5)));
    }

    public async Task<Result> ApplyActiveModPreferencesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await InternalApplyActivePreferencesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
#if DEBUG
            throw;
#endif

            _logger.Error(e, "An error occured when applying active preferences");
            return Result.Error(new SimpleNotification("Failed to apply saved preferences", e.Message, null));
        }
    }

    private async Task<Result> InternalApplyActivePreferencesAsync(CancellationToken cancellationToken)
    {
        await _userPreferencesService.SetModPreferencesAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new SimpleNotification("Saved preferences applied",
            $"Mod preferences written to 3DMigoto {Constants.UserIniFileName}",
            TimeSpan.FromSeconds(5)));
    }
}
