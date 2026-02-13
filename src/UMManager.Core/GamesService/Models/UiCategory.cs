namespace UMManager.Core.GamesService.Models;

public sealed record UiCategory(Guid Id, string Name, Uri? ImageUri, int Order, bool IsHidden);
