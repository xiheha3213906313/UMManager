using System.Diagnostics;
using System.IO;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.GamesService.JsonModels;

namespace UMManager.Core.GamesService.Models;

[DebuggerDisplay("{" + nameof(DisplayName) + "}" + " {" + nameof(InternalName) + "}")]
public class CharacterSkin : ICharacterSkin
{
    public bool IsDefault { get; internal set; }
    public int Rarity { get; internal set; } = -1;
    public Uri? ImageUri { get; set; } = null;
    public string DisplayName { get; set; } = null!;
    public InternalName InternalName { get; init; } = null!;
    public ICharacter Character { get; internal set; } = null!;
    public DateTime? ReleaseDate { get; internal set; } = null;


    internal static CharacterSkin FromJson(ICharacter character, JsonCharacterSkin jsonSkin)
    {
        var internalName = jsonSkin.InternalName ??
                           throw new Character.InvalidJsonConfigException("InternalName can never be missing or null");

        var characterSkin = new CharacterSkin
        {
            InternalName = new InternalName(internalName),
            DisplayName = jsonSkin.DisplayName ?? internalName,
            Rarity = jsonSkin.Rarity is >= 0 and <= 5 ? jsonSkin.Rarity.Value : -1,
            ReleaseDate = DateTime.TryParse(jsonSkin.ReleaseDate, out var date) ? date : DateTime.MaxValue,
            Character = character,
            ImageUri = !string.IsNullOrWhiteSpace(jsonSkin.Image) &&
                       Path.IsPathRooted(jsonSkin.Image) &&
                       File.Exists(jsonSkin.Image)
                ? new Uri(jsonSkin.Image)
                : null
        };

        return characterSkin;
    }

    public ICharacterSkin Clone()
    {
        return new CharacterSkin
        {
            IsDefault = IsDefault,
            Rarity = Rarity,
            ImageUri = ImageUri,
            DisplayName = DisplayName,
            InternalName = InternalName,
            Character = Character,
            ReleaseDate = ReleaseDate
        };
    }

    internal CharacterSkin()
    {
    }

    public CharacterSkin(ICharacter character)
    {
        Character = character;
    }

    public bool Equals(INameable? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return InternalName.Equals(other.InternalName);
    }

    public bool Equals(ICharacterSkin? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return InternalName.Equals(other.InternalName);
    }
}
