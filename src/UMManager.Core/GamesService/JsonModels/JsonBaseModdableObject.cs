namespace UMManager.Core.GamesService.JsonModels;

internal class JsonBaseModdableObject : JsonBaseNameable
{
    public bool? IsMultiMod { get; set; }
    public string? Image { get; set; }
    public string? ModCategory { get; set; }
}
