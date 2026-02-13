using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService;

namespace UMManager.WinUI.Services;

public sealed class GlobalSearchService
{
    private readonly IGameService _gameService;
    private readonly ISkinManagerService _skinManagerService;

    public GlobalSearchService(IGameService gameService, ISkinManagerService skinManagerService)
    {
        _gameService = gameService;
        _skinManagerService = skinManagerService;
    }
}