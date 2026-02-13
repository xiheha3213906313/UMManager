using UMManager.Core.GamesService;
using UMManager.WinUI.Contracts.Services;
using UMManager.WinUI.Helpers;
using UMManager.WinUI.Models.Options;
using Newtonsoft.Json;
using Serilog;

namespace UMManager.WinUI.Services.AppManagement;

public class SelectedGameService
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger _logger;

    private const string _defaultApplicationDataFolder = "ApplicationData";

    private const string ConfigFile = "game.json";
    private readonly string _configPath;

    private const string Genshin = "Genshin";
    private const string Honkai = "Honkai";
    private const string WuWa = "WuWa";
    private const string ZZZ = "ZZZ";


    public SelectedGameService(ILocalSettingsService localSettingsService, ILogger logger)
    {
        _localSettingsService = localSettingsService;
        _logger = logger;
        var configFolder = ConfigStorageHelper.GetConfigRoot();
        _configPath = Path.Combine(configFolder, ConfigFile);
    }

    private string GetGameSpecificSettingsFolderName(string game)
    {
        return Path.Combine(_defaultApplicationDataFolder + "_" + game);
    }

    public async Task SetSelectedGame(string game)
    {
        if (!IsValidGame(game))
            throw new ArgumentException("Invalid game name.");

        if (await GetSelectedGameAsync() == game)
            return;

        _localSettingsService.SetApplicationDataFolderName(GetGameSpecificSettingsFolderName(game));
        await SaveSelectedGameAsync(game).ConfigureAwait(false);
    }

    public async Task InitializeAsync()
    {
        if (!File.Exists(_configPath))
        {
            await SaveSelectedGameAsync(Genshin);
        }


        var selectedGame = await GetSelectedGameAsync();

        _localSettingsService.SetApplicationDataFolderName(GetGameSpecificSettingsFolderName(selectedGame));
    }

    public async Task<string> GetSelectedGameAsync()
    {
        if (!File.Exists(_configPath))
            return Genshin;

        var selectedGame = JsonConvert.DeserializeObject<SelectedGameModel>(await File.ReadAllTextAsync(_configPath));

        if (selectedGame == null || !IsValidGame(selectedGame.SelectedGame))
            return Genshin;


        return selectedGame.SelectedGame;
    }

    public async Task<SupportedGames[]> GetNotSelectedGameAsync()
    {
        var selectedGame = await GetSelectedGameAsync();

        return selectedGame switch
        {
            Genshin => [SupportedGames.Honkai, SupportedGames.WuWa, SupportedGames.ZZZ],
            Honkai => [SupportedGames.Genshin, SupportedGames.WuWa, SupportedGames.ZZZ],
            WuWa => [SupportedGames.Genshin, SupportedGames.Honkai, SupportedGames.ZZZ],
            ZZZ => [SupportedGames.Genshin, SupportedGames.Honkai, SupportedGames.WuWa],
            _ => throw new ArgumentOutOfRangeException()
        };
    }


    public Task SaveSelectedGameAsync(string game)
    {
        if (!IsValidGame(game))
            throw new ArgumentException("Invalid game name.");


        var selectedGame = new SelectedGameModel
        {
            SelectedGame = game
        };

        return File.WriteAllTextAsync(_configPath, JsonConvert.SerializeObject(selectedGame, Formatting.Indented));
    }

    public async Task<bool> IsInitializedForGameAsync(string game)
    {
        if (!IsValidGame(game))
            throw new ArgumentException("Invalid game name.");

        string? oldGame = null;
        if (!_localSettingsService.GameScopedSettingsLocation.Equals(GetGameSpecificSettingsFolderName(game),
                StringComparison.OrdinalIgnoreCase))
        {
            oldGame = game;
            _localSettingsService.SetApplicationDataFolderName(GetGameSpecificSettingsFolderName(game));
        }


        var modManagerOptions = await Task
            .Run(() => _localSettingsService.ReadSettingAsync<ModManagerOptions>(ModManagerOptions.Section));

        var ret = modManagerOptions is not null && !string.IsNullOrEmpty(modManagerOptions.GimiRootFolderPath) &&
                  !string.IsNullOrEmpty(modManagerOptions.ModsFolderPath);

        if (oldGame != null)
            _localSettingsService.SetApplicationDataFolderName(GetGameSpecificSettingsFolderName(oldGame));
        return ret;
    }

    private bool IsValidGame(string game)
    {
        if (Enum.TryParse<SupportedGames>(game, out _))
            return true;

        return game is Genshin or Honkai or WuWa;
    }
}

public class SelectedGameModel
{
    public string SelectedGame { get; set; } = "Genshin";
}
