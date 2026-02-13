﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using FuzzySharp;
using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService.Exceptions;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.GamesService.JsonModels;
using UMManager.Core.GamesService.Models;
using UMManager.Core.GamesService.Requests;
using UMManager.Core.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Serilog;
using JsonElement = UMManager.Core.GamesService.JsonModels.JsonElement;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace UMManager.Core.GamesService;

public class GameService : IGameService
{
    private readonly ILogger _logger;
    private readonly ILanguageLocalizer _localizer;

    private InitializationOptions _options = null!;
    private DirectoryInfo _assetsDirectory = null!;
    private DirectoryInfo? _languageOverrideDirectory;

    private GameSettingsManager _gameSettingsManager = null!;


    public GameInfo GameInfo { get; private set; } = null!;
    public string GameName => GameInfo.GameName;
    public string GameShortName => GameInfo.GameShortName;
    public string GameIcon => GameInfo.GameIcon;
    public string GameServiceSettingsFilePath => _gameSettingsManager.SettingsFilePath;
    public Uri GameBananaUrl => GameInfo.GameBananaUrl;
    public event EventHandler? Initialized;

    private readonly EnableableList<ICharacter> _characters = new();

    private readonly EnableableList<INpc> _npcs = new();
    private readonly EnableableList<IGameObject> _gameObjects = new();
    private readonly EnableableList<IWeapon> _weapons = new();

    private readonly List<IModdableObject> _duplicateInternalNames = new();

    private readonly List<ICategory> _categories = new();

    private Elements Elements { get; set; } = null!;

    private Classes Classes { get; set; } = null!;

    private Regions Regions { get; set; } = null!;

    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public GameService(ILogger logger, ILanguageLocalizer localizer)
    {
        _logger = logger;
        _localizer = localizer;
        _localizer.LanguageChanged += LanguageChangedHandler;
    }

    private bool _initialized;

    public Task InitializeAsync(string assetsDirectory, string localSettingsDirectory)
    {
        var options = new InitializationOptions
        {
            AssetsDirectory = assetsDirectory,
            LocalSettingsDirectory = localSettingsDirectory
        };

        return InitializeAsync(options);
    }


    public async Task InitializeAsync(InitializationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options, nameof(options));
        _options = options;

        if (_initialized)
            throw new InvalidOperationException("GameService is already initialized");

        _assetsDirectory = new DirectoryInfo(options.AssetsDirectory);
        if (!_assetsDirectory.Exists)
            throw new DirectoryNotFoundException($"Directory not found at path: {_assetsDirectory.FullName}");

        var settingsDirectory = new DirectoryInfo(options.LocalSettingsDirectory);
        settingsDirectory.Create();

        _gameSettingsManager = new GameSettingsManager(_logger, settingsDirectory);

        await InitializeGameInfoAsync().ConfigureAwait(false);

        await InitializeRegionsAsync().ConfigureAwait(false);

        await InitializeElementsAsync().ConfigureAwait(false);

        await InitializeClassesAsync().ConfigureAwait(false);

        await InitializeCharactersAsync().ConfigureAwait(false);

        await InitializeNpcsAsync().ConfigureAwait(false);

        await InitializeObjectsAsync().ConfigureAwait(false);

        await InitializeWeaponsAsync().ConfigureAwait(false);

        await MapCategoriesLanguageOverrideAsync().ConfigureAwait(false);

        CheckIfDuplicateInternalNameExists();

        _initialized = true;
        Initialized?.Invoke(this, EventArgs.Empty);
    }


    public static async Task<GameInfo?> GetGameInfoAsync(SupportedGames game, string? languageCode = null)
    {
        var gameAssetDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Games", game.ToString());

        var gameFilePath = GetLocalizedGameJsonPath(gameAssetDir, languageCode);

        if (!File.Exists(gameFilePath))
            return null;

        var jsonGameInfo =
            JsonSerializer.Deserialize<JsonGame>(await File.ReadAllTextAsync(gameFilePath).ConfigureAwait(false));

        if (jsonGameInfo is null)
            throw new InvalidOperationException($"{gameFilePath} file is empty");

        return new GameInfo(jsonGameInfo, new DirectoryInfo(gameAssetDir));
    }

    private static string GetLocalizedGameJsonPath(string gameAssetDir, string? languageCode)
    {
        const string gameFileName = "game.json";

        var normalizedLanguageCode = (languageCode ?? string.Empty).Trim().ToLowerInvariant();
        if (!normalizedLanguageCode.IsNullOrEmpty() && normalizedLanguageCode != "en-us")
        {
            var localizedPath = Path.Combine(gameAssetDir, "Languages", normalizedLanguageCode, gameFileName);
            if (File.Exists(localizedPath))
                return localizedPath;
        }

        return Path.Combine(gameAssetDir, gameFileName);
    }

    public async Task<ICollection<InternalName>> PreInitializedReadModObjectsAsync(string assetsDirectory)
    {
        if (_initialized)
        {
            _logger.Warning("GameService is already initialized");
            return GetAllModdableObjects(GetOnly.Both).Select(x => x.InternalName).ToList();
        }

        _assetsDirectory = new DirectoryInfo(assetsDirectory);
        if (!_assetsDirectory.Exists)
            throw new DirectoryNotFoundException($"Directory not found at path: {_assetsDirectory.FullName}");

        var modDirectories = new List<InternalName>();

        foreach (var predefinedCategory in Category.GetAllPredefinedCategories())
        {
            var jsonFilePath = Path.Combine(_assetsDirectory.FullName, predefinedCategory.InternalName.Id + ".json");
            if (!File.Exists(jsonFilePath))
                continue;
            try
            {
                var json = await File.ReadAllTextAsync(jsonFilePath).ConfigureAwait(false);
                var jsonBaseNameables = JsonSerializer.Deserialize<IEnumerable<JsonBaseNameable>>(json,
                    _jsonSerializerOptions);

                jsonBaseNameables ??= Array.Empty<JsonBaseNameable>();

                foreach (var jsonBaseNameable in jsonBaseNameables)
                {
                    if (jsonBaseNameable.InternalName.IsNullOrEmpty())
                        continue;

                    var internalName = new InternalName(jsonBaseNameable.InternalName);
                    if (modDirectories.Contains(internalName))
                        continue;
                    modDirectories.Add(internalName);
                }
            }
            catch (Exception)
            {
                _logger.Warning("Error while reading {JsonFile} during PreInitializedReadModObjectsAsync",
                    jsonFilePath);
            }
        }

        return modDirectories;
    }

    public async Task UpdateCharacterAsync(InternalName internalName, UpdateCharacterRequest request)
    {
        ArgumentNullException.ThrowIfNull(internalName);

        if (!request.AnyValuesSet)
            throw new ArgumentException("No values were set for editing character");

        var character = GetCharacterByIdentifier(internalName.Id, includeDisabledCharacters: true);
        if (character is null)
            throw new ModObjectNotFoundException($"Character with internal name {internalName} not found");

        if (request.DisplayName.IsSet)
            character.DisplayName = request.DisplayName.ValueToSet;

        if (request.IsMultiMod.IsSet)
            character.IsMultiMod = request.IsMultiMod.ValueToSet;

        if (request.Keys.IsSet)
            character.Keys = request.Keys.ValueToSet;

        if (character is Character internalCharacter)
        {
            if (request.Rarity.IsSet)
                internalCharacter.Rarity = request.Rarity.ValueToSet < 0 ? internalCharacter.DefaultCharacter.Rarity : request.Rarity.ValueToSet;

            if (request.Element.IsSet)
            {
                var value = request.Element.ValueToSet?.Trim();
                internalCharacter.Element = string.IsNullOrWhiteSpace(value)
                    ? internalCharacter.DefaultCharacter.Element
                    : Elements.AllElements.FirstOrDefault(e => e.InternalNameEquals(value)) ?? internalCharacter.DefaultCharacter.Element;
            }

            if (request.Class.IsSet)
            {
                var value = request.Class.ValueToSet?.Trim();
                internalCharacter.Class = string.IsNullOrWhiteSpace(value)
                    ? internalCharacter.DefaultCharacter.Class
                    : Classes.AllClasses.FirstOrDefault(c => c.InternalNameEquals(value)) ?? internalCharacter.DefaultCharacter.Class;
            }

            if (request.Region.IsSet)
            {
                var regions = request.Region.ValueToSet?.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ??
                              Array.Empty<string>();
                internalCharacter.Regions = regions.Length == 0
                    ? internalCharacter.DefaultCharacter.Regions.ToArray()
                    : Regions.AllRegions.Where(r => regions.Any(x => r.InternalNameEquals(x))).ToArray();
            }
        }


        if (request.Image.IsSet)
        {
            var imageToSet = request.Image.ValueToSet;
            var image = await _gameSettingsManager.SetCharacterImageAsync(character.InternalName, imageToSet).ConfigureAwait(false);
            if (image is not null)
                character.ImageUri = image;
            else
                character.ImageUri = null;
        }

        await SaveCharacterToSettingsAsync(character).ConfigureAwait(false);
        if (character is Character internalCharacterAfterSave)
            internalCharacterAfterSave.DefaultCharacter = internalCharacterAfterSave.Clone();
    }

    public async Task DisableCharacterAsync(ICharacter character)
    {
        await _gameSettingsManager.SetCharacterEnabledAsync(character.InternalName, enabled: false).ConfigureAwait(false);
        var internalCharacter = _characters.FirstOrDefault(x => x.ModdableObject.InternalName == character.InternalName);

        if (internalCharacter is not null)
            internalCharacter.IsEnabled = false;
        else
            throw new InvalidOperationException($"Character with internal name {character?.InternalName} not found");
    }

    public async Task EnableCharacterAsync(ICharacter character)
    {
        await _gameSettingsManager.SetCharacterEnabledAsync(character.InternalName, enabled: true).ConfigureAwait(false);
        var internalCharacter = _characters.FirstOrDefault(x => x.ModdableObject.InternalName == character.InternalName);

        if (internalCharacter is not null)
            internalCharacter.IsEnabled = true;
        else
            throw new InvalidOperationException($"Character with internal name {character?.InternalName} not found");
    }

    public async Task ResetOverrideForCharacterAsync(ICharacter character)
    {
        character.DisplayName = character.DefaultCharacter.DisplayName;
        character.ImageUri = character.DefaultCharacter.ImageUri;
        character.Keys = character.DefaultCharacter.Keys;
        character.IsMultiMod = character.DefaultCharacter.IsMultiMod;

        if (character is Character internalCharacter)
        {
            internalCharacter.Rarity = internalCharacter.DefaultCharacter.Rarity;
            internalCharacter.Element = internalCharacter.DefaultCharacter.Element;
            internalCharacter.Class = internalCharacter.DefaultCharacter.Class;
            internalCharacter.Regions = internalCharacter.DefaultCharacter.Regions.ToArray();
        }

        await SaveCharacterToSettingsAsync(character).ConfigureAwait(false);
    }

    private async Task SaveCharacterToSettingsAsync(ICharacter character)
    {
        var wrapper = _characters.FirstOrDefault(c => c.ModdableObject.InternalNameEquals(character.InternalName));
        var isEnabled = wrapper?.IsEnabled ?? true;
        var record = CreateRecordFromCharacter(character, isEnabled);
        await _gameSettingsManager.UpsertCharacterAsync(record).ConfigureAwait(false);
    }

    private JsonCharacterRecord CreateRecordFromCharacter(ICharacter character, bool isEnabled)
    {
        var assetsImageFolder = Path.Combine(_assetsDirectory.FullName, "Images", "Characters");
        var assetsSkinFolder = Path.Combine(_assetsDirectory.FullName, "Images", "AltCharacterSkins");

        var image = character.ImageUri?.IsFile == true ? character.ImageUri.LocalPath : null;
        if (!image.IsNullOrEmpty() && IsInFolder(image, assetsImageFolder))
            image = Path.GetFileName(image);

        var skins = character.Skins
            .Where(s => !s.IsDefault)
            .Select(s =>
            {
                var skinImage = s.ImageUri?.IsFile == true ? s.ImageUri.LocalPath : null;
                if (!skinImage.IsNullOrEmpty() && IsInFolder(skinImage, assetsSkinFolder))
                    skinImage = Path.GetFileName(skinImage);

                return new JsonCharacterSkin
                {
                    InternalName = s.InternalName.Id,
                    DisplayName = s.DisplayName,
                    Image = skinImage
                };
            })
            .ToArray();

        var releaseDate = character.ReleaseDate is null ||
                          character.ReleaseDate == DateTime.MinValue ||
                          character.ReleaseDate == DateTime.MaxValue
            ? null
            : character.ReleaseDate.Value.ToString("O");

        return new JsonCharacterRecord
        {
            InternalName = character.InternalName.Id,
            DisplayName = character.DisplayName.IsNullOrEmpty() ? null : character.DisplayName,
            Keys = character.Keys.Count == 0 ? null : character.Keys.ToArray(),
            ReleaseDate = releaseDate,
            Image = image,
            Rarity = character.Rarity < 0 ? null : character.Rarity,
            Element = character.Element.Equals(Models.Element.NoneElement()) ? null : character.Element.InternalName.Id,
            Class = character.Class.Equals(Models.Class.NoneClass()) ? null : character.Class.InternalName.Id,
            Region = character.Regions.Count == 0 ? null : character.Regions.Select(r => r.InternalName.Id).ToArray(),
            IsMultiMod = character.IsMultiMod ? true : null,
            IsEnabled = isEnabled,
            InGameSkins = skins.Length == 0 ? null : skins
        };
    }

    private static bool IsInFolder(string filePath, string folderPath)
    {
        try
        {
            var fullFile = Path.GetFullPath(filePath);
            var fullFolder = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                             Path.DirectorySeparatorChar;
            return fullFile.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<UiCategory>> GetUiCategoriesAsync()
    {
        var categories = await _gameSettingsManager.GetUiCategoriesAsync().ConfigureAwait(false);
        return categories
            .Select(MapUiCategory)
            .OrderBy(c => c.Order)
            .ToList();
    }

    public async Task<UiCategory> CreateUiCategoryAsync(string name, Uri? image)
    {
        var category = await _gameSettingsManager.CreateUiCategoryAsync(name, image).ConfigureAwait(false);
        return MapUiCategory(category);
    }

    public async Task<UiCategory> UpdateUiCategoryAsync(Guid categoryId, UpdateUiCategoryRequest request)
    {
        var category = await _gameSettingsManager.UpdateUiCategoryAsync(categoryId, request).ConfigureAwait(false);
        return MapUiCategory(category);
    }

    public Task DeleteUiCategoryAsync(Guid categoryId) => _gameSettingsManager.DeleteUiCategoryAsync(categoryId);

    public Task<IReadOnlyList<Guid>> GetCharacterTagsAsync(InternalName internalName) =>
        _gameSettingsManager.GetCharacterTagsAsync(internalName);

    public Task SetCharacterTagsAsync(InternalName internalName, IEnumerable<Guid> tags) =>
        _gameSettingsManager.SetCharacterTagsAsync(internalName, tags);

    private static UiCategory MapUiCategory(JsonUiCategory category)
    {
        Uri? image = null;
        if (!category.Image.IsNullOrEmpty() && Uri.TryCreate(category.Image, UriKind.Absolute, out var imageUri) &&
            imageUri.IsFile)
        {
            image = imageUri;
        }

        return new UiCategory(category.Id, category.Name, image, category.Order, category.IsHidden ?? false);
    }

    public List<IModdableObject> GetModdableObjects(ICategory category, GetOnly getOnlyStatus = GetOnly.Enabled)
    {
        if (category.ModCategory == ModCategory.Character)
            return _characters.GetOfType(getOnlyStatus).Cast<IModdableObject>().ToList();

        if (category.ModCategory == ModCategory.NPC)
            return _npcs.GetOfType(getOnlyStatus).Cast<IModdableObject>().ToList();

        if (category.ModCategory == ModCategory.Object)
            return _gameObjects.GetOfType(getOnlyStatus).Cast<IModdableObject>().ToList();

        if (category.ModCategory == ModCategory.Weapons)
            return _weapons.GetOfType(getOnlyStatus).Cast<IModdableObject>().ToList();

        throw new ArgumentException($"Category {category.InternalName} is not supported");
    }


    public List<IModdableObject> GetAllModdableObjects(GetOnly getOnlyStatus = GetOnly.Enabled)
    {
        var moddableObjects = new List<IModdableObject>();

        moddableObjects.AddRange(_characters.GetOfType(getOnlyStatus));
        moddableObjects.AddRange(_npcs.GetOfType(getOnlyStatus));
        moddableObjects.AddRange(_gameObjects.GetOfType(getOnlyStatus));
        moddableObjects.AddRange(_weapons.GetOfType(getOnlyStatus));

        return moddableObjects;
    }

    public List<T> GetAllModdableObjectsAsCategory<T>(GetOnly getOnlyStatus = GetOnly.Enabled) where T : IModdableObject
    {
        if (typeof(T) == typeof(ICharacter))
            return _characters.GetOfType(getOnlyStatus).Cast<T>().ToList();

        if (typeof(T) == typeof(INpc))
            return _npcs.GetOfType(getOnlyStatus).Cast<T>().ToList();

        if (typeof(T) == typeof(IGameObject))
            return _gameObjects.GetOfType(getOnlyStatus).Cast<T>().ToList();

        if (typeof(T) == typeof(IWeapon))
            return _weapons.GetOfType(getOnlyStatus).Cast<T>().ToList();


        throw new ArgumentException($"Type {typeof(T)} is not supported");
    }

    // TODO: Not happy with this, should consider reworking custom characters and overrides
    public async Task<ICharacter> CreateCharacterAsync(CreateCharacterRequest characterRequest)
    {
        if (characterRequest.InternalName?.Id == null || characterRequest.InternalName.Id.IsNullOrEmpty())
            throw new InvalidModdableObjectException("InternalName must not be null or empty");

        if (GetAllModdableObjects(GetOnly.Both).Any(m => m.InternalNameEquals(characterRequest.InternalName)))
            throw new InvalidModdableObjectException("A moddable object with the same internal name already exists. InternalName must be unique");

        if (characterRequest.Image is not null)
        {
            if (!characterRequest.Image.IsFile || !File.Exists(characterRequest.Image.LocalPath))
                throw new InvalidModdableObjectException("Image must be a valid existing filesystem file");

            var imageExtension = Path.GetExtension(characterRequest.Image.LocalPath);
            if (imageExtension.IsNullOrEmpty())
                throw new InvalidModdableObjectException("Image must have an extension");

            if (Constants.SupportedImageExtensions.All(x => x != imageExtension))
                throw new InvalidModdableObjectException("Image must have a supported extension. Supported extensions are: " +
                                                         string.Join(", ", Constants.SupportedImageExtensions));
        }

        Character character;
        try
        {
            var jsonCharacter = new JsonCharacter
            {
                InternalName = characterRequest.InternalName.Id,
                DisplayName = characterRequest.DisplayName,
                Keys = characterRequest.Keys?.ToArray(),
                Class = characterRequest.Class,
                Element = characterRequest.Element,
                Region = characterRequest.Region?.ToArray(),
                Rarity = characterRequest.Rarity,
                ReleaseDate = characterRequest.ReleaseDate?.ToString("O"),
                IsMultiMod = characterRequest.IsMultiMod,
                Image = null,
                InGameSkins = null
            };

            character = Character
                .FromJson(jsonCharacter, isCustomObject: true)
                .SetRegion(Regions.AllRegions)
                .SetElement(Elements.AllElements)
                .SetClass(Classes.AllClasses)
                .CreateCharacter();
        }
        catch (Exception e)
        {
            throw new InvalidModdableObjectException($"Failed to create character: {e.Message}", e);
        }

        if (characterRequest.Image is not null)
        {
            try
            {
                var destinationImage = await _gameSettingsManager
                    .SetCharacterImageAsync(character.InternalName, characterRequest.Image)
                    .ConfigureAwait(false);
                character.ImageUri = destinationImage;
            }
            catch (Exception e)
            {
                throw new InvalidModdableObjectException($"Failed to copy character image: {e.Message}", e);
            }
        }

        character.DefaultCharacter = character.Clone();

        try
        {
            await _gameSettingsManager.UpsertCharacterAsync(CreateRecordFromCharacter(character, isEnabled: true)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            throw new InvalidModdableObjectException($"Failed to save character: {e.Message}", e);
        }


        _characters.Add(new Enableable<ICharacter>(character, isEnabled: true));

        return character;
    }

    private readonly JsonSerializerSettings _jsonCharacterExportSettings = new()
    {
        Formatting = Formatting.Indented,
        TypeNameHandling = TypeNameHandling.Auto,
        NullValueHandling = NullValueHandling.Ignore,
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
        Converters = new List<JsonConverter> { new StringEnumConverter() }
    };

    public async Task<(string json, ICharacter character)> CreateJsonCharacterExportAsync(CreateCharacterRequest characterRequest)
    {
        var internalCreateRequest = new JsonCharacter()
        {
            InternalName = characterRequest.InternalName.Id,
            DisplayName = characterRequest.DisplayName,
            Keys = characterRequest.Keys?.ToArray(),
            Class = characterRequest.Class,
            Element = characterRequest.Element,
            Image = characterRequest.Image?.LocalPath,
            IsMultiMod = characterRequest.IsMultiMod,
            Region = characterRequest.Region?.ToArray(),
            Rarity = characterRequest.Rarity,
            ReleaseDate = characterRequest.ReleaseDate?.ToString("O"),
            InGameSkins = null
        };

        if (characterRequest.Image is not null)
        {
            if (!characterRequest.Image.IsFile || !File.Exists(characterRequest.Image.LocalPath))
                throw new InvalidModdableObjectException("Image must be a valid existing filesystem file");

            var imageExtension = Path.GetExtension(characterRequest.Image.LocalPath);

            if (imageExtension.IsNullOrEmpty())
                throw new InvalidModdableObjectException("Image must have an extension");

            if (Constants.SupportedImageExtensions.All(x => x != imageExtension))
                throw new InvalidModdableObjectException("Image must have a supported extension. Supported extensions are: " +
                                                         string.Join(", ", Constants.SupportedImageExtensions));
        }


        Character character;
        try
        {
            character = Character
                .FromJson(internalCreateRequest, isCustomObject: true)
                .SetRegion(Regions.AllRegions)
                .SetElement(Elements.AllElements)
                .SetClass(Classes.AllClasses)
                .CreateCharacter();
        }
        catch (Exception e)
        {
            throw new InvalidModdableObjectException($"Failed to create character: {e.Message}", e);
        }

        var isValidImage = characterRequest.Image is not null && characterRequest.Image.IsFile && File.Exists(characterRequest.Image.LocalPath);


        var jsonCharacter = new JsonCharacter()
        {
            InternalName = character.InternalName.Id,
            DisplayName = character.DisplayName.IsNullOrEmpty() ? null : character.DisplayName,
            Keys = character.Keys.Count == 0 ? null : character.Keys.ToArray(),
            Class = Class.NoneClass().Equals(character.Class) ? null : character.Class.InternalName.Id,
            Element = Element.NoneElement().Equals(character.Element) ? null : character.Element.InternalName.Id,
            Region = character.Regions.Count == 0 ? null : character.Regions.Select(x => x.InternalName.Id).ToArray(),
            Image = isValidImage
                ? character.InternalName + Path.GetExtension(characterRequest.Image!.LocalPath)
                : null,
            IsMultiMod = character.IsMultiMod ? true : null,
            Rarity = character.Rarity,
            ReleaseDate = character.ReleaseDate?.ToString("O").Split('.').ElementAtOrDefault(0),
            InGameSkins = null
        };

        if (isValidImage)
            character.ImageUri = character.ImageUri = characterRequest.Image;

        var json = JsonConvert.SerializeObject(jsonCharacter, _jsonCharacterExportSettings);

        return (json, character);
    }

    public async Task<ICharacter> RenameCharacterAsync(InternalName internalName, InternalName newInternalName)
    {
        if (internalName.Equals(newInternalName))
            return GetAllModdableObjectsAsCategory<ICharacter>(GetOnly.Both)
                       .FirstOrDefault(c => c.InternalNameEquals(internalName))
                   ?? throw new ModObjectNotFoundException($"Character with internal name {internalName} not found");

        var existingCustomCharacter = GetAllModdableObjectsAsCategory<ICharacter>(GetOnly.Both)
            .FirstOrDefault(c => c.InternalNameEquals(internalName));

        if (existingCustomCharacter is null)
            throw new ModObjectNotFoundException($"Character with internal name {internalName} not found");

        if (existingCustomCharacter is not Character existingCharacter)
            throw new InvalidModdableObjectException("Currently only characters are supported");

        if (GetAllModdableObjects(GetOnly.Both).Any(mo => !mo.InternalNameEquals(internalName) && mo.InternalNameEquals(newInternalName)))
            throw new InvalidOperationException($"Internal name {newInternalName.Id} already in use");

        await _gameSettingsManager.RenameCharacterAsync(internalName, newInternalName).ConfigureAwait(false);

        var imageUri = _gameSettingsManager.GetCharacterImagePath(newInternalName);

        var renamedCharacter = CloneCharacterForRename(existingCharacter, newInternalName);
        if (imageUri is not null)
            renamedCharacter.ImageUri = imageUri;
        renamedCharacter.DefaultCharacter = renamedCharacter.Clone();

        var wrapper = _characters.FirstOrDefault(c => c.ModdableObject.InternalNameEquals(internalName));
        if (wrapper is null)
            throw new InvalidOperationException($"Character with internal name {internalName} not found in internal list");

        var index = _characters.IndexOf(wrapper);
        var isEnabled = wrapper.IsEnabled;
        _characters.Remove(wrapper);
        _characters.Insert(index, new Enableable<ICharacter>(renamedCharacter, isEnabled));

        return renamedCharacter;
    }

    public async Task<ICharacter> DeleteCharacterAsync(InternalName internalName)
    {
        var character = GetAllModdableObjectsAsCategory<ICharacter>(GetOnly.Both)
            .FirstOrDefault(c => c.InternalNameEquals(internalName));

        if (character is null)
            throw new ModObjectNotFoundException($"Character with internal name {internalName} not found");


        await _gameSettingsManager.DeleteCharacterAsync(character.InternalName).ConfigureAwait(false);

        var result = _characters.Remove(character);

        if (!result)
            _logger.Warning("Failed to remove character {InternalName} from internal GameService list", internalName);

        return character;
    }

    public async Task AddCharacterSkinAsync(InternalName characterInternalName, AddCharacterSkinRequest request)
    {
        ArgumentNullException.ThrowIfNull(characterInternalName);
        ArgumentNullException.ThrowIfNull(request);

        var character = GetAllModdableObjectsAsCategory<ICharacter>(GetOnly.Both)
            .FirstOrDefault(c => c.InternalNameEquals(characterInternalName));

        if (character is null)
            throw new ModObjectNotFoundException($"Character with internal name {characterInternalName} not found");

        if (request.Image is null || !request.Image.IsFile || !File.Exists(request.Image.LocalPath))
            throw new InvalidModdableObjectException("Skin image must be a valid existing filesystem file");

        var skinInternalName = new InternalName($"Skin_{Guid.NewGuid():N}");
        var index = character.Skins.Count(s => !s.IsDefault) + 1;
        var displayName = _localizer.CurrentLanguage.LanguageCode == "zh-cn" ? $"皮肤 {index}" : $"Skin {index}";

        var newSkin = new CharacterSkin(character)
        {
            InternalName = skinInternalName,
            DisplayName = displayName,
            ImageUri = request.Image,
            Rarity = character.Rarity,
            ReleaseDate = character.ReleaseDate,
            IsDefault = false
        };

        character.Skins.Add(newSkin);
        await SaveCharacterToSettingsAsync(character).ConfigureAwait(false);
    }

    public async Task SetCharacterSkinImageAsync(InternalName characterInternalName, InternalName skinInternalName, Uri image)
    {
        ArgumentNullException.ThrowIfNull(characterInternalName);
        ArgumentNullException.ThrowIfNull(skinInternalName);
        ArgumentNullException.ThrowIfNull(image);

        if (!image.IsFile || !File.Exists(image.LocalPath))
            throw new InvalidModdableObjectException("Skin image must be a valid existing filesystem file");

        var character = GetAllModdableObjectsAsCategory<ICharacter>(GetOnly.Both)
            .FirstOrDefault(c => c.InternalNameEquals(characterInternalName));

        if (character is null)
            throw new ModObjectNotFoundException($"Character with internal name {characterInternalName} not found");

        var skin = character.Skins.FirstOrDefault(s => s.InternalNameEquals(skinInternalName));
        if (skin is null || skin.IsDefault)
            throw new InvalidModdableObjectException("Only non-default skins can be updated");

        if (skin is CharacterSkin internalSkin)
            internalSkin.ImageUri = image;
        await SaveCharacterToSettingsAsync(character).ConfigureAwait(false);
    }

    public async Task SetCharacterSkinDisplayNameAsync(InternalName characterInternalName, InternalName skinInternalName, string displayName)
    {
        ArgumentNullException.ThrowIfNull(characterInternalName);
        ArgumentNullException.ThrowIfNull(skinInternalName);
        ArgumentNullException.ThrowIfNull(displayName);

        displayName = displayName.Trim();
        if (displayName.Length == 0)
            throw new InvalidModdableObjectException("Skin display name cannot be empty");

        var character = GetAllModdableObjectsAsCategory<ICharacter>(GetOnly.Both)
            .FirstOrDefault(c => c.InternalNameEquals(characterInternalName));

        if (character is null)
            throw new ModObjectNotFoundException($"Character with internal name {characterInternalName} not found");

        var skin = character.Skins.FirstOrDefault(s => s.InternalNameEquals(skinInternalName));
        if (skin is null)
            throw new InvalidModdableObjectException("Skin not found");

        skin.DisplayName = displayName;
        await SaveCharacterToSettingsAsync(character).ConfigureAwait(false);
    }

    public async Task DeleteCharacterSkinAsync(InternalName characterInternalName, InternalName skinInternalName)
    {
        ArgumentNullException.ThrowIfNull(characterInternalName);
        ArgumentNullException.ThrowIfNull(skinInternalName);

        var character = GetAllModdableObjectsAsCategory<ICharacter>(GetOnly.Both)
            .FirstOrDefault(c => c.InternalNameEquals(characterInternalName));

        if (character is null)
            throw new ModObjectNotFoundException($"Character with internal name {characterInternalName} not found");

        if (character is Character internalCharacter && internalCharacter.Skins is not List<ICharacterSkin>)
            internalCharacter.Skins = internalCharacter.Skins.ToList();

        var skin = character.Skins.FirstOrDefault(s => s.InternalNameEquals(skinInternalName));
        if (skin is null || skin.IsDefault)
            throw new InvalidModdableObjectException("Default skin cannot be deleted");

        character.Skins.Remove(skin);
        await SaveCharacterToSettingsAsync(character).ConfigureAwait(false);
    }

    private static Character CloneCharacterForRename(Character character, InternalName newInternalName)
    {
        var renamedCharacter = character.Clone(newInternalName);

        var additionalSkins = renamedCharacter.Skins.Where(s => !s.IsDefault).ToArray();

        var defaultSkin = new CharacterSkin(renamedCharacter)
        {
            InternalName = new InternalName("Default_" + renamedCharacter.InternalName),
            ImageUri = renamedCharacter.ImageUri,
            DisplayName = "Default",
            Rarity = renamedCharacter.Rarity,
            ReleaseDate = renamedCharacter.ReleaseDate,
            IsDefault = true
        };

        foreach (var skin in additionalSkins)
        {
            if (skin is CharacterSkin internalSkin)
                internalSkin.Character = renamedCharacter;
        }

        renamedCharacter.Skins = new List<ICharacterSkin> { defaultSkin }.Concat(additionalSkins).ToArray();
        return renamedCharacter;
    }

    public ICharacter? QueryCharacter(string keywords, IEnumerable<ICharacter>? restrictToCharacters = null,
        int minScore = 100)
    {
        var searchResult = QueryCharacters(keywords, restrictToCharacters, minScore);

        return searchResult.Any(kv => kv.Value >= minScore) ? searchResult.MaxBy(x => x.Value).Key : null;
    }

    public ICharacter? GetCharacterByIdentifier(string internalName, bool includeDisabledCharacters = false)
    {
        var characters =
            GetAllModdableObjectsAsCategory<ICharacter>(includeDisabledCharacters ? GetOnly.Both : GetOnly.Enabled);

        return characters.FirstOrDefault(x => x.InternalNameEquals(internalName));
    }

    public IModdableObject? GetModdableObjectByIdentifier(InternalName? internalName,
        GetOnly getOnlyStatus = GetOnly.Enabled)
    {
        return GetAllModdableObjects(getOnlyStatus).FirstOrDefault(x => x.InternalNameEquals(internalName));
    }


    public Dictionary<ICharacter, int> QueryCharacters(string searchQuery,
        IEnumerable<ICharacter>? restrictToCharacters = null, int minScore = 100,
        bool includeDisabledCharacters = false)
    {
        var searchResult = new Dictionary<ICharacter, int>();
        searchQuery = searchQuery.ToLower().Trim();

        var charactersToSearch = restrictToCharacters ?? (includeDisabledCharacters
            ? GetAllModdableObjectsAsCategory<ICharacter>(GetOnly.Both)
            : GetAllModdableObjectsAsCategory<ICharacter>());

        foreach (var character in charactersToSearch)
        {
            var result = GetBaseSearchResult(searchQuery, character);

            // A character can have multiple keys, so we take the best one. The keys are only used to help with searching
            if (character.Keys.Count > 0)
            {
                var bestKeyMatch = character.Keys.Max(key => Fuzz.Ratio(key, searchQuery));
                result += bestKeyMatch;
            }

            if (character.Keys.Any(key => key.Equals(searchQuery, StringComparison.CurrentCultureIgnoreCase)))
                result += 100;

            if (result < minScore) continue;

            searchResult.Add(character, result);
        }

        return searchResult;
    }


    public Dictionary<IModdableObject, int> QueryModdableObjects(string searchQuery, ICategory? category = null,
        int minScore = 100)
    {
        var searchResult = new Dictionary<IModdableObject, int>();
        searchQuery = searchQuery.ToLower().Trim();


        if (category?.ModCategory == ModCategory.Character)
            return QueryCharacters(searchQuery, GetAllModdableObjectsAsCategory<ICharacter>(), minScore)
                .ToDictionary(x => x.Key as IModdableObject, x => x.Value);


        var charactersToSearch = category is null
            ? GetAllModdableObjects()
            : GetModdableObjects(category);


        foreach (var moddableObject in charactersToSearch)
        {
            var result = GetBaseSearchResult(searchQuery, moddableObject);

            if (result < minScore) continue;

            searchResult.Add(moddableObject, result);
        }

        return searchResult;
    }

    private static int GetBaseSearchResult(string searchQuery, INameable character)
    {
        var loweredDisplayName = character.DisplayName.ToLower();

        var result = 0;

        // If the search query contains the display name, we give it a lot of points
        var sameChars = loweredDisplayName.Split().Count(searchQuery.Contains);
        result += sameChars * 60;

        var splitNames = loweredDisplayName.Split();
        var sameStartChars = 0;
        var bestResultOfNames = 0;
        // This loop will give points for each name that starts with the same chars as the search query
        foreach (var name in splitNames)
        {
            sameStartChars = 0;
            foreach (var @char in searchQuery)
            {
                if (name.ElementAtOrDefault(sameStartChars) == default(char)) continue;

                if (name[sameStartChars] != @char) continue;

                sameStartChars++;
                if (sameStartChars > bestResultOfNames)
                    bestResultOfNames = sameStartChars;
            }
        }

        result += sameStartChars * 11; // Give more points for same start chars

        result += loweredDisplayName.Split(' ', options: StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Max(name => Fuzz.PartialRatio(name, searchQuery)); // Do a partial ratio for each name
        return result;
    }


    public List<IGameElement> GetElements() => Elements.AllElements.ToList();

    public List<IGameClass> GetClasses() => Classes.AllClasses.ToList();

    public List<IRegion> GetRegions() => Regions.AllRegions.ToList();

    [Obsolete($"Use {nameof(GetAllModdableObjectsAsCategory)} instead")]
    public List<ICharacter> GetCharacters(bool includeDisabled = false) =>
        includeDisabled ? _characters.GetOfType(GetOnly.Both).ToList() : _characters.WhereEnabled().ToList();

    [Obsolete($"Use {nameof(GetAllModdableObjectsAsCategory)} instead")]
    public List<ICharacter> GetDisabledCharacters() => _characters.WhereDisabled().ToList();

    public List<ICategory> GetCategories() => new(_categories);


    public bool IsMultiMod(IModdableObject moddableObject) =>
        moddableObject.IsMultiMod || IsMultiMod(moddableObject.InternalName);

    private static bool IsMultiMod(string modInternalName)
    {
        var legacyPredefinedMultiMod = new List<string> { "gliders", "weapons", "others" };

        return legacyPredefinedMultiMod.Any(name => name.Equals(modInternalName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task InitializeGameInfoAsync()
    {
        var gameFilePath = GetLocalizedGameJsonPath(_assetsDirectory.FullName, _localizer.CurrentLanguage.LanguageCode);
        if (!File.Exists(gameFilePath))
            throw new FileNotFoundException($"game.json File not found at path: {gameFilePath}");

        var jsonGameInfo =
            JsonSerializer.Deserialize<JsonGame>(await File.ReadAllTextAsync(gameFilePath).ConfigureAwait(false));

        if (jsonGameInfo is null)
            throw new InvalidOperationException($"{gameFilePath} file is empty");

        GameInfo = new GameInfo(jsonGameInfo, _assetsDirectory);
    }

    private async Task InitializeRegionsAsync()
    {
        var regionFileName = "regions.json";
        var regionsFilePath = Path.Combine(_assetsDirectory.FullName, regionFileName);

        if (!File.Exists(regionsFilePath))
            throw new FileNotFoundException($"{regionFileName} File not found at path: {regionsFilePath}");

        var regions =
            JsonSerializer.Deserialize<IEnumerable<JsonRegion>>(
                await File.ReadAllTextAsync(regionsFilePath).ConfigureAwait(false),
                _jsonSerializerOptions) ?? throw new InvalidOperationException("Regions file is empty");

        Regions = Regions.InitializeRegions(regions);

        if (LanguageOverrideAvailable())
            await MapDisplayNames(regionFileName, Regions.AllRegions).ConfigureAwait(false);
    }

    private async Task InitializeCharactersAsync()
    {
        const string characterFileName = "characters.json";
        var assetsImageFolder = Path.Combine(_assetsDirectory.FullName, "Images", "Characters");
        var assetsSkinFolder = Path.Combine(_assetsDirectory.FullName, "Images", "AltCharacterSkins");

        var storedCharacters = await _gameSettingsManager.GetAllCharactersAsync().ConfigureAwait(false);
        if (storedCharacters.Count == 0)
        {
            var jsonCharacters = await SerializeAsync<JsonCharacter>(characterFileName).ConfigureAwait(false);
            var seededCharacters = new List<ICharacter>();

            foreach (var jsonCharacter in jsonCharacters)
            {
                var character = Character
                    .FromJson(jsonCharacter, isCustomObject: true)
                    .SetRegion(Regions.AllRegions)
                    .SetElement(Elements.AllElements)
                    .SetClass(Classes.AllClasses)
                    .CreateCharacter(imageFolder: assetsImageFolder, characterSkinImageFolder: assetsSkinFolder);

                seededCharacters.Add(character);
            }

            seededCharacters.Add(getOthersCharacter());
            seededCharacters.Add(getGlidersCharacter());
            seededCharacters.Add(getWeaponsCharacter());

            if (LanguageOverrideAvailable())
                await MapDisplayNames(characterFileName, seededCharacters).ConfigureAwait(false);

            var seedRecords = seededCharacters.Select(c => CreateRecordFromCharacter(c, isEnabled: true, assetsImageFolder, assetsSkinFolder)).ToList();
            await _gameSettingsManager.ReplaceAllCharactersAsync(seedRecords).ConfigureAwait(false);

            storedCharacters = await _gameSettingsManager.GetAllCharactersAsync().ConfigureAwait(false);
        }

        _characters.Clear();

        foreach (var record in storedCharacters.Values)
        {
            var character = CreateCharacterFromRecord(record, assetsImageFolder, assetsSkinFolder);
            _characters.Add(new Enableable<ICharacter>(character, record.IsEnabled));

            if (!_options.CharacterSkinsAsCharacters) continue;

            foreach (var skin in character.ClearAndReturnSkins())
            {
                var newCharacter = character.FromCharacterSkin(skin);
                _characters.Add(new Enableable<ICharacter>(newCharacter, record.IsEnabled));
            }
        }

        _categories.Add(Category.CreateForCharacter());

        JsonCharacterRecord CreateRecordFromCharacter(ICharacter character, bool isEnabled, string assetsCharacterImageFolder,
            string assetsSkinFolder)
        {
            var image = character.ImageUri?.IsFile == true ? character.ImageUri.LocalPath : null;
            if (!image.IsNullOrEmpty() && IsInFolder(image, assetsCharacterImageFolder))
                image = Path.GetFileName(image);

            var skins = character.Skins
                .Where(s => !s.IsDefault)
                .Select(s =>
                {
                    var skinImage = s.ImageUri?.IsFile == true ? s.ImageUri.LocalPath : null;
                    if (!skinImage.IsNullOrEmpty() && IsInFolder(skinImage, assetsSkinFolder))
                        skinImage = Path.GetFileName(skinImage);

                    return new JsonCharacterSkin
                    {
                        InternalName = s.InternalName.Id,
                        DisplayName = s.DisplayName,
                        Image = skinImage
                    };
                })
                .ToArray();

            var releaseDate = character.ReleaseDate is null ||
                              character.ReleaseDate == DateTime.MinValue ||
                              character.ReleaseDate == DateTime.MaxValue
                ? null
                : character.ReleaseDate.Value.ToString("O");

            var record = new JsonCharacterRecord
            {
                InternalName = character.InternalName.Id,
                DisplayName = character.DisplayName.IsNullOrEmpty() ? null : character.DisplayName,
                Keys = character.Keys.Count == 0 ? null : character.Keys.ToArray(),
                ReleaseDate = releaseDate,
                Image = image,
                Rarity = character.Rarity < 0 ? null : character.Rarity,
                Element = character.Element.Equals(Models.Element.NoneElement()) ? null : character.Element.InternalName.Id,
                Class = character.Class.Equals(Models.Class.NoneClass()) ? null : character.Class.InternalName.Id,
                Region = character.Regions.Count == 0 ? null : character.Regions.Select(r => r.InternalName.Id).ToArray(),
                IsMultiMod = character.IsMultiMod ? true : null,
                IsEnabled = isEnabled,
                InGameSkins = skins.Length == 0 ? null : skins
            };

            return record;
        }

        Character CreateCharacterFromRecord(JsonCharacterRecord record, string assetsCharacterImageFolder, string assetsSkinFolder)
        {
            var jsonCharacter = new JsonCharacter
            {
                InternalName = record.InternalName,
                DisplayName = record.DisplayName,
                Keys = record.Keys,
                ReleaseDate = record.ReleaseDate,
                Image = record.Image,
                Rarity = record.Rarity,
                Element = record.Element,
                Class = record.Class,
                Region = record.Region,
                IsMultiMod = record.IsMultiMod,
                InGameSkins = record.InGameSkins
            };

            var character = Character
                .FromJson(jsonCharacter, isCustomObject: true)
                .SetRegion(Regions.AllRegions)
                .SetElement(Elements.AllElements)
                .SetClass(Classes.AllClasses)
                .CreateCharacter(imageFolder: assetsCharacterImageFolder, characterSkinImageFolder: assetsSkinFolder);

            ApplyAbsoluteImages(character, record);
            return character;
        }

        void ApplyAbsoluteImages(Character character, JsonCharacterRecord record)
        {
            if (Uri.TryCreate(record.Image, UriKind.Absolute, out var imageUri) && imageUri.IsFile && File.Exists(imageUri.LocalPath))
            {
                character.ImageUri = imageUri;
            }

            if (record.InGameSkins is null || record.InGameSkins.Length == 0)
                return;

            foreach (var jsonSkin in record.InGameSkins)
            {
                if (jsonSkin.InternalName.IsNullOrEmpty())
                    continue;
                if (!Uri.TryCreate(jsonSkin.Image, UriKind.Absolute, out var skinImageUri) || !skinImageUri.IsFile)
                    continue;
                if (!File.Exists(skinImageUri.LocalPath))
                    continue;

                var existingSkin = character.Skins.FirstOrDefault(s => s.InternalNameEquals(jsonSkin.InternalName));
                if (existingSkin is CharacterSkin internalSkin)
                    internalSkin.ImageUri = skinImageUri;
            }
        }

        bool IsInFolder(string filePath, string folderPath)
        {
            try
            {
                var fullFile = Path.GetFullPath(filePath);
                var fullFolder = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                 Path.DirectorySeparatorChar;
                return fullFile.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    private async Task InitializeNpcsAsync()
    {
        var npcFileName = "npcs.json";
        var imageFolderName = Path.Combine(_assetsDirectory.FullName, "Images", "Npcs");

        var jsonNpcs = await SerializeAsync<JsonNpc>(npcFileName).ConfigureAwait(false);

        foreach (var jsonNpc in jsonNpcs)
        {
            var npc = Npc.FromJson(jsonNpc, imageFolderName);

            _npcs.Add(npc);
        }

        if (LanguageOverrideAvailable())
            await MapDisplayNames(npcFileName, _npcs.ToEnumerable()).ConfigureAwait(false);

        if (_npcs.Any())
            _categories.Add(Category.CreateForNpc());
    }

    private async Task InitializeObjectsAsync()
    {
        var objectFileName = "objects.json";
        var imageFolderName = Path.Combine(_assetsDirectory.FullName, "Images",
            Path.GetFileNameWithoutExtension(objectFileName));

        var objects = await BaseModdableObjectMapper(objectFileName, imageFolderName, Category.CreateForObjects())
            .ConfigureAwait(false);

        if (LanguageOverrideAvailable())
            await MapDisplayNames(objectFileName, objects).ConfigureAwait(false);

        if (objects.Any())
        {
            _categories.Add(Category.CreateForObjects());
            foreach (var moddableObject in objects)
                _gameObjects.Add(new GameObject(moddableObject));
        }
        else
            _logger.Debug("No gameObjects found in {ObjectFileName}", objectFileName);
    }

    //private async Task InitializeGlidersAsync()
    private async Task InitializeWeaponsAsync()
    {
        if (!Classes.AllClasses.Any())
            throw new InvalidOperationException("Classes must be initialized before weapons");

        const string weaponFileName = "weapons.json";
        var imageFolderName = Path.Combine(_assetsDirectory.FullName, "Images", "Weapons");
        var jsonWeapons = await SerializeAsync<JsonWeapon>(weaponFileName).ConfigureAwait(false);

        foreach (var jsonWeapon in jsonWeapons)
        {
            var weapon = Weapon.FromJson(jsonWeapon, imageFolderName, jsonWeapon.Rarity, Classes.AllClasses);

            _weapons.Add(new Enableable<IWeapon>(weapon));
        }

        if (LanguageOverrideAvailable())
            await MapDisplayNames(weaponFileName, _weapons.ToEnumerable()).ConfigureAwait(false);

        if (_weapons.Any())
            _categories.Add(Category.CreateForWeapons());
    }
    //private async Task InitializeCustomAsync()


    private async Task InitializeClassesAsync()
    {
        const string classFileName = "weaponClasses.json";

        var classes = await SerializeAsync<JsonClasses>(classFileName).ConfigureAwait(false);

        Classes = new Classes("Classes");

        Classes.Initialize(classes, _assetsDirectory.FullName);

        if (LanguageOverrideAvailable())
            await MapDisplayNames(classFileName, Classes.AllClasses).ConfigureAwait(false);
    }

    private async Task InitializeElementsAsync()
    {
        const string elementsFileName = "elements.json";

        var elements = await SerializeAsync<JsonElement>(elementsFileName).ConfigureAwait(false);

        Elements = new Elements("Elements");

        Elements.Initialize(elements, _assetsDirectory.FullName);

        if (LanguageOverrideAvailable())
            await MapDisplayNames(elementsFileName, Elements.AllElements).ConfigureAwait(false);
    }

    private async Task MapCategoriesLanguageOverrideAsync()
    {
        const string categoriesFileName = "categories.json";

        if (LanguageOverrideAvailable())
            await MapDisplayNames(categoriesFileName, GetCategories()).ConfigureAwait(false);
    }

    [MemberNotNullWhen(true, nameof(_languageOverrideDirectory))]
    private bool LanguageOverrideAvailable()
    {
        var currentLanguage = _localizer.CurrentLanguage;

        _languageOverrideDirectory =
            new DirectoryInfo(Path.Combine(_assetsDirectory.FullName, "Languages", currentLanguage.LanguageCode));

        if (currentLanguage.LanguageCode == "en-us")
            return false;


        return _languageOverrideDirectory.Exists && _languageOverrideDirectory.GetFiles().Any();
    }


    private Task<IEnumerable<T>> SerializeAsync<T>(string fileName, bool throwIfNotFound = true)
    {
        var objFilePath = Path.Combine(_assetsDirectory.FullName, fileName);

        return SerializeFileAsync<T>(filePath: objFilePath, throwIfNotFound: throwIfNotFound);
    }

    private async Task<IEnumerable<T>> SerializeFileAsync<T>(string filePath, bool throwIfNotFound = true)
    {
        if (!File.Exists(filePath))
        {
            if (throwIfNotFound)
                throw new FileNotFoundException($"File not found at path: {filePath}");
            else
                return Array.Empty<T>();
        }

        return JsonSerializer.Deserialize<IEnumerable<T>>(
            await File.ReadAllTextAsync(filePath).ConfigureAwait(false),
            _jsonSerializerOptions) ?? throw new InvalidOperationException($"{filePath} is empty");
    }

    public string OtherCharacterInternalName => "Others";

    private Character getOthersCharacter()
    {
        var character = new Character
        {
            //Id = _otherCharacterId,
            InternalName = new InternalName(OtherCharacterInternalName),
            DisplayName = OtherCharacterInternalName,
            ReleaseDate = DateTime.MinValue,
            Rarity = -1,
            Regions = new List<IRegion>(),
            Keys = new[] { "others", "unknown" },
            ImageUri = new Uri(Path.Combine(_assetsDirectory.FullName, "Images", "Characters", "Others.png")),
            Element = Elements.AllElements.First(),
            Class = Classes.AllClasses.First(),
            IsMultiMod = true
        };
        AddDefaultSkin(character);
        return character;
    }

    public string GlidersCharacterInternalName => "Gliders";

    private Character getGlidersCharacter()
    {
        var character = new Character
        {
            //Id = _glidersCharacterId,
            InternalName = new InternalName(GlidersCharacterInternalName),
            DisplayName = GlidersCharacterInternalName,
            ReleaseDate = DateTime.MinValue,
            Rarity = -1,
            Regions = new List<IRegion>(),
            Keys = new[] { "gliders", "glider", "wings" },
            ImageUri = new Uri(Path.Combine(_assetsDirectory.FullName, "Images", "Characters",
                "Gliders.webp")),
            Element = Elements.AllElements.First(),
            Class = Classes.AllClasses.First(),
            IsMultiMod = true
        };
        AddDefaultSkin(character);
        return character;
    }

    public string WeaponsCharacterInternalName => "Weapons";

    private Character getWeaponsCharacter()
    {
        var character = new Character
        {
            //Id = _weaponsCharacterId,
            InternalName = new InternalName(WeaponsCharacterInternalName),
            DisplayName = WeaponsCharacterInternalName,
            ReleaseDate = DateTime.MinValue,
            Rarity = -1,
            Regions = new List<IRegion>(),
            Keys = new[] { "weapon", "claymore", "sword", "polearm", "catalyst", "bow" },
            ImageUri = new Uri(Path.Combine(_assetsDirectory.FullName, "Images", "Characters",
                "Weapons.webp")),
            Element = Elements.AllElements.First(),
            Class = Classes.AllClasses.First(),
            IsMultiMod = true
        };

        AddDefaultSkin(character);
        return character;
    }

    private void AddDefaultSkin(ICharacter character)
    {
        character.Skins.Add(new CharacterSkin(character)
        {
            InternalName = new InternalName("Default_" + character.InternalName),
            DisplayName = GetDefaultSkinDisplayName(),
            Rarity = character.Rarity,
            ReleaseDate = character.ReleaseDate,
            Character = character,
            IsDefault = true
        });
    }

    private string GetDefaultSkinDisplayName()
    {
        return _localizer.CurrentLanguage.LanguageCode == "zh-cn" ? "默认" : "Default";
    }


    private async Task MapDisplayNames(string fileName, IEnumerable<INameable> nameables)
    {
        var filePath = Path.Combine(_languageOverrideDirectory!.FullName, fileName);

        if (!File.Exists(filePath))
        {
            _logger.Debug("File {FileName} not found at path: {FilePath}, no translation available", fileName,
                filePath);
            return;
        }

        var json = JsonSerializer.Deserialize<ICollection<JsonOverride>>(
            await File.ReadAllTextAsync(filePath).ConfigureAwait(false),
            _jsonSerializerOptions);

        if (json is null)
            return;

        foreach (var nameable in nameables)
        {
            var jsonOverride = json.FirstOrDefault(x => nameable.InternalNameEquals(x.InternalName));
            if (jsonOverride is null)
            {
                _logger.Debug("Nameable {NameableName} not found in {FilePath}", nameable.InternalName, filePath);
                continue;
            }

            if (!jsonOverride.DisplayName.IsNullOrEmpty())
            {
                nameable.DisplayName = jsonOverride.DisplayName;
            }


            if (nameable is IImageSupport imageSupportedValue && !jsonOverride.Image.IsNullOrEmpty())
                _logger.Warning("Image override is not implemented");

            if (nameable is ICategory category && !jsonOverride.DisplayNamePlural.IsNullOrEmpty())
            {
                category.DisplayNamePlural = jsonOverride.DisplayNamePlural;
            }

            if (nameable is not ICharacter character) continue;

            character.Skins.ForEach(skin =>
            {
                var skinOverride =
                    jsonOverride?.InGameSkins?.FirstOrDefault(x =>
                        x.InternalName is not null &&
                        skin.InternalNameEquals(x.InternalName));

                if (skinOverride is null)
                {
                    _logger.Debug("Skin {SkinName} not found in {FilePath}", skin.InternalName, filePath);
                    return;
                }

                if (skinOverride.DisplayName.IsNullOrEmpty()) return;

                skin.DisplayName = skinOverride.DisplayName;

                if (skinOverride.Image.IsNullOrEmpty()) return;
                _logger.Warning("Image override is not implemented for character skins");
            });

            if (jsonOverride.RemoveExistingKeys is not null && jsonOverride.Keys is not null &&
                jsonOverride.Keys.Count != 0)
            {
                if (jsonOverride.RemoveExistingKeys.Value)
                {
                    character.Keys.Clear();
                }

                foreach (var jsonOverrideKey in jsonOverride.Keys)
                {
                    if (character.Keys.Contains(jsonOverrideKey)) continue;
                    character.Keys.Add(jsonOverrideKey);
                }
            }
        }
    }

    private async void LanguageChangedHandler(object? sender, EventArgs args)
    {
        if (!_initialized)
            return;

        _logger.Debug("Language changed to {Language}", _localizer.CurrentLanguage.LanguageCode);

        _languageOverrideDirectory =
            new DirectoryInfo(Path.Combine(_assetsDirectory.FullName, "Languages",
                _localizer.CurrentLanguage.LanguageCode));

        if (_localizer.CurrentLanguage.LanguageCode == "en-us")
        {
            _languageOverrideDirectory = new DirectoryInfo(Path.Combine(_assetsDirectory.FullName));
        }


        await MapDisplayNames("characters.json", _characters.ToEnumerable()).ConfigureAwait(false);
        await MapDisplayNames("npcs.json", _npcs.ToEnumerable()).ConfigureAwait(false);
        await MapDisplayNames("elements.json", Elements.AllElements).ConfigureAwait(false);
        await MapDisplayNames("weaponClasses.json", Classes.AllClasses).ConfigureAwait(false);
        await MapDisplayNames("regions.json", Regions.AllRegions).ConfigureAwait(false);
    }

    private async Task<IModdableObject[]> BaseModdableObjectMapper(string jsonFileName, string imageFolder,
        ICategory category)
    {
        var jsonBaseModdableObjects = await SerializeAsync<JsonBaseModdableObject>(jsonFileName).ConfigureAwait(false);

        var list = new List<IModdableObject>();

        foreach (var jsonBaseModdableObject in jsonBaseModdableObjects)
        {
            var moddableObject = BaseModdableObject
                .FromJson(jsonBaseModdableObject, category, imageFolder);

            list.Add(moddableObject);
        }

        return list.ToArray();
    }

    private void CheckIfDuplicateInternalNameExists()
    {
        var allNameables = GetAllModdableObjects(GetOnly.Both);

        var duplicates = allNameables
            .GroupBy(x => x.InternalName)
            .Where(g => g.Count() > 1)
            .Select(y => y.Key)
            .ToList();

        if (duplicates.Any())
            throw new InvalidOperationException(
                $"Duplicate internal names found: {string.Join(", ", duplicates)}");
    }
}

public class Regions
{
    private readonly List<Region> _regions;
    public IReadOnlyCollection<IRegion> AllRegions => _regions;

    private Regions(List<Region> regions)
    {
        _regions = regions;
    }

    internal static Regions InitializeRegions(IEnumerable<JsonRegion> regions)
    {
        var regionsList = new List<Region>();
        foreach (var region in regions)
        {
            if (string.IsNullOrWhiteSpace(region.InternalName) ||
                string.IsNullOrWhiteSpace(region.DisplayName))
                throw new InvalidOperationException("Region has invalid data");

            regionsList.Add(new Region(region.InternalName, region.DisplayName));
        }

        return new Regions(regionsList);
    }

    internal void InitializeLanguageOverrides(IEnumerable<JsonRegion> regions)
    {
        foreach (var region in regions)
        {
            var regionToOverride = _regions.FirstOrDefault(x => x.InternalName == region.InternalName);
            if (regionToOverride == null)
            {
                Log.Debug("Region {RegionName} not found in regions list", region.InternalName);
                continue;
            }

            if (string.IsNullOrWhiteSpace(region.DisplayName))
            {
                Log.Warning("Region {RegionName} has invalid display name", region.InternalName);
                continue;
            }

            regionToOverride.DisplayName = region.DisplayName;
        }
    }
}

internal class Classes : BaseMapper<Class>
{
    public IReadOnlyList<IGameClass> AllClasses => Values;

    public Classes(string name) : base(name)
    {
        Values.Add(new Class()
        {
            DisplayName = "None",
            InternalName = new InternalName("None")
        });
    }
}

internal class Elements : BaseMapper<Element>
{
    public IReadOnlyList<IGameElement> AllElements => Values;

    public Elements(string name) : base(name)
    {
        Values.Add(new Element()
        {
            DisplayName = "None",
            InternalName = new InternalName("None")
        });
    }
}

internal abstract class BaseMapper<T> where T : class, INameable, new()
{
    public string Name { get; }
    protected readonly List<T> Values;

    protected BaseMapper(string name)
    {
        Values = new List<T>();
        Name = name;
    }

    internal void Initialize(IEnumerable<JsonBaseNameable> newValues, string assetsDirectory)
    {
        foreach (var value in newValues)
        {
            if (string.IsNullOrWhiteSpace(value.InternalName) ||
                string.IsNullOrWhiteSpace(value.DisplayName))
                throw new InvalidOperationException($"{Name} has invalid data");

            var newValue = new T()
            {
                DisplayName = value.DisplayName,
                InternalName = new InternalName(value.InternalName)
            };

            // check if T is of type IImageSupport
            if (newValue is IImageSupport imageSupport && value is JsonElement element &&
                !element.Image.IsNullOrEmpty())
            {
                var imageFolder = Path.Combine(assetsDirectory, "Images", Name);

                var imageUri = Uri.TryCreate(Path.Combine(imageFolder, element.Image), UriKind.Absolute,
                    out var uri)
                    ? uri
                    : null;

                if (imageUri is null)
                    Log.Warning("Image {Image} for {Name} {ElementName} is invalid", element.Image, Name,
                        newValue.DisplayName);

                if (!File.Exists(imageUri?.LocalPath ?? string.Empty))
                {
                    Log.Debug("Image {Image} for {Name} {ElementName} does not exist", element.Image, Name,
                        newValue.DisplayName);
                    return;
                }

                imageSupport.ImageUri = imageUri;
            }

            Values.Add(newValue);
        }
    }

    internal void InitializeLanguageOverrides(IEnumerable<JsonBaseNameable> overrideValues)
    {
        foreach (var value in overrideValues)
        {
            var regionToOverride = Values.FirstOrDefault(x => x.InternalName == value.InternalName);
            if (regionToOverride == null)
            {
                Log.Debug("Region {Name} not found in regions list", Name);
                continue;
            }

            if (string.IsNullOrWhiteSpace(value.DisplayName))
            {
                Log.Warning("Region {Name} has invalid display name", Name);
                continue;
            }

            regionToOverride.DisplayName = value.DisplayName;
        }
    }
}

internal sealed class Enableable<T> where T : IModdableObject
{
    public Enableable(T moddableObject, bool isEnabled = true)
    {
        ModdableObject = moddableObject;
        IsEnabled = isEnabled;
    }

    public bool IsEnabled { get; internal set; }
    public T ModdableObject { get; init; }

    public static implicit operator T(Enableable<T> enableable) => enableable.ModdableObject;

    public static implicit operator Enableable<T>(T moddableObject) => new(moddableObject);

    public override string ToString() => $"Enabled: {IsEnabled} | {ModdableObject.InternalName}";
}

internal sealed class EnableableList<T> : List<Enableable<T>> where T : IModdableObject
{
    public EnableableList(IEnumerable<Enableable<T>> enableables) : base(enableables)
    {
    }

    public EnableableList()
    {
    }

    public static implicit operator List<T>(EnableableList<T> enableableList) =>
        enableableList.Select(x => x.ModdableObject).ToList();

    public static implicit operator EnableableList<T>(List<T> moddableObjects) =>
        new(moddableObjects.Select(x => new Enableable<T>(x)));

    public IEnumerable<T> ToEnumerable() => this.Select(x => x.ModdableObject);

    public IEnumerable<T> WhereEnabled() => this.Where(x => x.IsEnabled).Select(x => x.ModdableObject);

    public IEnumerable<T> WhereDisabled() => this.Where(x => !x.IsEnabled).Select(x => x.ModdableObject);

    public IEnumerable<T> GetOfType(GetOnly type) => type switch
    {
        GetOnly.Enabled => WhereEnabled(),
        GetOnly.Disabled => WhereDisabled(),
        _ => ToEnumerable()
    };


    /// <inheritdoc cref="List{T}.Remove"/>
    public bool Remove(T moddableObject)
    {
        var enabledWrapper = this.FirstOrDefault(e => moddableObject.Equals(e.ModdableObject));

        if (enabledWrapper is null)
            return false;

        return base.Remove(enabledWrapper);
    }
}
