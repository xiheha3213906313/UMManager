using Newtonsoft.Json;

namespace UMManager.Core.GamesService.Interfaces;

public interface ICharacterSkin : IRarity, IImageSupport, INameable, IEquatable<ICharacterSkin>
{
    /// <summary>
    /// Character this skin belongs to
    /// </summary>
    [JsonIgnore]
    public ICharacter Character { get; }

    /// <summary>
    /// Is default skin for character
    /// </summary>
    public bool IsDefault { get; }

    public DateTime? ReleaseDate { get; }

    public ICharacterSkin Clone();
}
