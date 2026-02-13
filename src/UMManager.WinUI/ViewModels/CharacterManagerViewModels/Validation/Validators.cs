﻿﻿﻿﻿﻿﻿﻿﻿﻿using UMManager.Core.GamesService;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.Helpers;

namespace UMManager.WinUI.ViewModels.CharacterManagerViewModels.Validation;

public static class Validators
{
    public static void AddInternalNameValidators(this FieldValidators<string> validators, ICollection<IModdableObject> allModdableObjects)
    {
        validators.AddRange([
            context => string.IsNullOrWhiteSpace(context.Value.Trim())
                ? new ValidationResult { Message = "Internal name cannot be empty" }
                : null,
            context => allModdableObjects.FirstOrDefault(m => m.InternalNameEquals(context.Value)) is { } existingModdableObject
                ? new ValidationResult { Message = $"Internal {context.Value} name already in use by {existingModdableObject.DisplayName}" }
                : null,
            context =>
            {
                var invalidFileSystemChars = Path.GetInvalidFileNameChars();


                foreach (var invalidChar in invalidFileSystemChars)
                {
                    if (context.Value.Contains(invalidChar))
                    {
                        return new ValidationResult
                        {
                            Message = $"Internal name contains invalid filesystem character '{invalidChar}'"
                        };
                    }
                }

                return null;
            }
        ]);
    }

    public static void AddDisplayNameValidators(this FieldValidators<string> validators, ICollection<IModdableObject> allModdableObjects)
    {
        validators.Add(context => allModdableObjects.FirstOrDefault(m => m.DisplayName.Equals(context.Value.Trim(), StringComparison.OrdinalIgnoreCase)) is
        { } existingModdableObject
            ? new ValidationResult
            {
                Message = $"Another mod object ({existingModdableObject.InternalName}) already uses this display name, this may cause oddities with search",
                Type = ValidationType.Warning
            }
            : null);
    }

    public static void AddImageValidators(this FieldValidators<Uri> validators)
    {
        validators.AddRange([
            context => context.Value == null! ? new ValidationResult { Message = "Image cannot be empty or null" } : null, context =>
                !context.Value!.IsFile || !File.Exists(context.Value.LocalPath)
                    ? new ValidationResult() { Message = "Image must be a valid existing image" }
                    : null,
            context =>
            {
                if (!context.Value!.IsFile) return null;
                var fileExtension = Path.GetExtension((string?)context.Value.LocalPath);

                var isSupportedExtension = Constants.SupportedImageExtensions.Contains(fileExtension, StringComparer.OrdinalIgnoreCase);

                return isSupportedExtension
                    ? null
                    : new ValidationResult
                    {
                        Message =
                            $"Image must be one of the following types: {string.Join(", ", Constants.SupportedImageExtensions)}. The extension {fileExtension} is not supported."
                    };
            }
        ]);
    }

    public static void AddRarityValidators(this List<ValidationCallback<int>> validators)
    {
        validators.AddRange([
            context => context.Value < 0 ? new ValidationResult { Message = "Rarity must be greater than -1" } : null,
            context => context.Value > 6 ? new ValidationResult { Message = "Rarity must be less than 7" } : null
        ]);
    }

    public static void AddElementValidators(this List<ValidationCallback<string>> validators, ICollection<IGameElement> elements)
    {
        validators.Add(context => elements.Any(e => e.InternalNameEquals(context.Value))
            ? null
            : new ValidationResult
            {
                Message = $"Element {context.Value} does not exist. Valid values {string.Join(',', elements.Select(e => e.InternalName))}"
            });
    }

    public static void AddClassValidators(this List<ValidationCallback<string>> validators, ICollection<IGameClass> classes)
    {
        validators.Add(context => classes.Any(c => c.InternalNameEquals(context.Value))
            ? null
            : new ValidationResult
            {
                Message = $"Class {context.Value} does not exist. Valid values {string.Join(',', classes.Select(c => c.InternalName))}"
            });
    }

    public static void AddRegionValidators(this FieldValidators<IReadOnlyCollection<string>> validators, ICollection<IRegion> regions)
    {
        validators.Add(context =>
        {
            foreach (var region in context.Value)
            {
                if (!regions.Any(r => r.InternalNameEquals(region)))
                {
                    return new ValidationResult
                    {
                        Message = $"Region {region} does not exist. Valid values {string.Join(',', regions.Select(r => r.InternalName))}"
                    };
                }
            }

            return null;
        });
    }

    public static void AddKeysValidators(this FieldValidators<IReadOnlyCollection<string>> validators, ICollection<ICharacter> allCharacters)
    {
        validators.Add(context =>
                {
                    var newKeys = context.Value;
                    if (newKeys.Count == 0) return null;


                    ValueTuple<ICharacter, string>? duplicateKey = null;
                    foreach (var character in allCharacters)
                    {
                        var duplicate = newKeys.FirstOrDefault(k => character.Keys.Contains(k, StringComparer.OrdinalIgnoreCase));
                        if (duplicate is not null)
                        {
                            duplicateKey = (character, duplicate);
                            break;
                        }
                    }

                    if (duplicateKey is null) return null;

                    return new ValidationResult
                    {
                        Message = $"Key {duplicateKey.Value.Item2} is already in use by {duplicateKey.Value.Item1.DisplayName}"
                    };
                });
    }
}
