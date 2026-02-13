using UMManager.Core.GamesService;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.Helpers;
using UMManager.WinUI.Services;
using UMManager.WinUI.ViewModels.CharacterManagerViewModels.Validation;

namespace UMManager.WinUI.ViewModels.CharacterManagerViewModels;

public sealed partial class EditCharacterForm : Form
{
    public EditCharacterForm()
    {
    }

    public void Initialize(ICharacter character, ICollection<IModdableObject> allModdableObjects)
    {
        allModdableObjects = allModdableObjects.Contains(character)
            ? allModdableObjects.Where(mo => !mo.Equals(character)).ToArray()
            : allModdableObjects;

        InternalName.ValidationRules.AddInternalNameValidators(allModdableObjects);
        InternalName.ReInitializeInput(character.InternalName);

        DisplayName.ValidationRules.AddDisplayNameValidators(allModdableObjects);
        DisplayName.ValidationRules.Add(context =>
            context.Value.Trim().IsNullOrEmpty() ? new ValidationResult { Message = "Display name cannot be empty" } : null);
        DisplayName.ReInitializeInput(character.DisplayName);

        Image.ValidationRules.AddImageValidators();
        Image.ReInitializeInput(character.ImageUri ?? ImageHandlerService.StaticPlaceholderImageUri);

        var rarity = character.Rarity < 0 ? 0 : character.Rarity;
        Rarity.ValidationRules.AddRarityValidators();
        Rarity.ReInitializeInput(rarity);

        IsMultiMod.ReInitializeInput(character.IsMultiMod);
        IsInitialized = true;
    }

    public InputField<Uri> Image { get; } = new(ImageHandlerService.StaticPlaceholderImageUri);
    public StringInputField InternalName { get; } = new(string.Empty);
    public StringInputField DisplayName { get; } = new(string.Empty);
    public InputField<int> Rarity { get; } = new(5);

    public InputField<bool> IsMultiMod { get; } = new(false);
}
