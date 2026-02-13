﻿﻿﻿﻿﻿﻿﻿﻿using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.GamesService.JsonModels;
using UMManager.Core.GamesService.Models;
using UMManager.Core.GamesService.Requests;
using UMManager.Core.Helpers;
using Newtonsoft.Json;
using Serilog;

namespace UMManager.Core.GamesService;

internal class GameSettingsManager
{
    private readonly ILogger _logger;
    internal string SettingsFilePath { get; }
    private readonly DirectoryInfo _settingsDirectory;
    private readonly DirectoryInfo _customImageFolder;
    internal readonly DirectoryInfo CharacterImageFolder;
    internal readonly DirectoryInfo UiCategoryImageFolder;

    private const string GameServiceFileName = "GameService";
    private GameServiceRootV2? _settings;

    internal GameSettingsManager(ILogger logger, DirectoryInfo settingsDirectory)
    {
        _settingsDirectory = settingsDirectory;
        _logger = logger.ForContext<GameSettingsManager>();
        SettingsFilePath = Path.Combine(settingsDirectory.FullName, GameServiceFileName + ".json");
        _customImageFolder = new DirectoryInfo(Path.Combine(settingsDirectory.FullName, "CustomImages"));
        _customImageFolder.Create();

        CharacterImageFolder = new DirectoryInfo(Path.Combine(_customImageFolder.FullName, "Characters"));
        CharacterImageFolder.Create();

        UiCategoryImageFolder = new DirectoryInfo(Path.Combine(_customImageFolder.FullName, "Categories"));
        UiCategoryImageFolder.Create();
    }

    internal async Task CleanupUnusedImagesAsync()
    {
        await ReadSettingsAsync(useCache: false).ConfigureAwait(false);

        var parsedImageUris = new HashSet<Uri>();

        foreach (var (internalName, character) in _settings.Characters)
        {
            var image = ParseAbsoluteFilePath(character.Image);
            if (image is null)
                continue;

            if (!File.Exists(image.LocalPath))
            {
                _logger.Warning("Image file for {InternalName} not found at {ImageFilePath}", internalName, image.LocalPath);
                continue;
            }

            parsedImageUris.Add(image);
        }

        foreach (var uiCategory in _settings.UiCategories)
        {
            var image = ParseAbsoluteFilePath(uiCategory.Image);
            if (image is null)
                continue;

            if (!File.Exists(image.LocalPath))
            {
                _logger.Warning("Image file for UiCategory {CategoryId} not found at {ImageFilePath}", uiCategory.Id, image.LocalPath);
                continue;
            }

            parsedImageUris.Add(image);
        }

        var foundImageFiles = CharacterImageFolder.GetFiles()
            .Concat(UiCategoryImageFolder.GetFiles())
            .Select(p => new Uri(p.FullName));

        var unusedImages = foundImageFiles.Except(parsedImageUris).ToList();

        if (unusedImages.Count == 0)
            return;

        _logger.Information("Found {UnusedImagesCount} unused images that will be deleted in AppData folder", unusedImages.Count);

        foreach (var unusedImage in unusedImages)
        {
            try
            {
                if (File.Exists(unusedImage.LocalPath))
                {
                    _logger.Debug("Deleting unused image file {UnusedImage}", unusedImage.LocalPath);
                    File.Delete(unusedImage.LocalPath);
                    _logger.Information("Deleted unused image file {UnusedImage}", unusedImage.LocalPath);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to delete unused image file {UnusedImage}", unusedImage.LocalPath);
            }
        }
    }

    [MemberNotNull(nameof(_settings))]
    internal async Task<GameServiceRootV2> ReadSettingsAsync(bool useCache = true)
    {
        if (useCache && _settings != null)
            return _settings;

        if (!File.Exists(SettingsFilePath))
        {
            _settings = new GameServiceRootV2();
            await SaveSettingsAsync().ConfigureAwait(false);
            return _settings;
        }

        try
        {
            var rootSettings =
                JsonConvert.DeserializeObject<GameServiceRootV2>(
                    await File.ReadAllTextAsync(SettingsFilePath).ConfigureAwait(false)) ??
                throw new InvalidOperationException("Settings file is empty");

            if (rootSettings.SchemaVersion != GameServiceRootV2.CurrentSchemaVersion)
                throw new InvalidOperationException($"Unsupported settings schema version: {rootSettings.SchemaVersion}");

            _settings = rootSettings;
        }
        catch (Exception e)
        {
            var invalidFilePath = SettingsFilePath;
            var newInvalidFilePath = Path.Combine(_settingsDirectory.FullName, GameServiceFileName + ".json.legacy");
            _logger.Error(e, "Failed to read settings file, renaming legacy file {InvalidFilePath} to {NewInvalidFilePath}", invalidFilePath,
                newInvalidFilePath);
            _settings = new GameServiceRootV2();

            if (File.Exists(invalidFilePath))
            {
                try
                {
                    if (File.Exists(newInvalidFilePath))
                        File.Delete(newInvalidFilePath);
                    File.Move(invalidFilePath, newInvalidFilePath);
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "Failed to rename invalid settings file {InvalidFilePath} to {NewInvalidFilePath}", invalidFilePath,
                        newInvalidFilePath);
                }
            }
        }


        return _settings;
    }

    private async Task SaveSettingsAsync()
    {
        if (_settings is null)
            return;

        await File.WriteAllTextAsync(SettingsFilePath, JsonConvert.SerializeObject(_settings, Formatting.Indented)).ConfigureAwait(false);
    }

    internal async Task<Dictionary<string, JsonCharacterRecord>> GetAllCharactersAsync()
    {
        var settings = await ReadSettingsAsync().ConfigureAwait(false);
        return new Dictionary<string, JsonCharacterRecord>(settings.Characters, StringComparer.OrdinalIgnoreCase);
    }

    internal async Task ReplaceAllCharactersAsync(IEnumerable<JsonCharacterRecord> characters)
    {
        await ReadSettingsAsync().ConfigureAwait(false);

        _settings.Characters = characters
            .Where(c => !c.InternalName.IsNullOrEmpty())
            .GroupBy(c => c.InternalName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToDictionary(c => c.InternalName, c => c, StringComparer.OrdinalIgnoreCase);

        await SaveSettingsAsync().ConfigureAwait(false);
    }

    internal async Task UpsertCharacterAsync(JsonCharacterRecord character)
    {
        await ReadSettingsAsync().ConfigureAwait(false);
        if (character.InternalName.IsNullOrEmpty())
            throw new InvalidOperationException("Character InternalName cannot be empty");

        _settings.Characters[character.InternalName] = character;
        await SaveSettingsAsync().ConfigureAwait(false);
    }

    internal async Task RenameCharacterAsync(InternalName internalName, InternalName newInternalName)
    {
        await ReadSettingsAsync().ConfigureAwait(false);
        if (internalName.Equals(newInternalName))
            return;

        if (!_settings.Characters.TryGetValue(internalName.Id, out var character))
            throw new InvalidOperationException("Character to rename not found. Try restarting UMManager");

        if (_settings.Characters.ContainsKey(newInternalName.Id))
            throw new InvalidOperationException($"Character '{newInternalName.Id}' already exists");

        if (_settings.CharacterTags.Remove(internalName.Id, out var tags))
            _settings.CharacterTags[newInternalName.Id] = tags;

        var existingImageUri = ParseAbsoluteFilePath(character.Image);
        if (existingImageUri is not null &&
            IsInFolder(existingImageUri.LocalPath, CharacterImageFolder.FullName) &&
            File.Exists(existingImageUri.LocalPath))
        {
            var fileExtension = Path.GetExtension(existingImageUri.LocalPath);
            var newImageUri = CreateCharacterImagePath(newInternalName, fileExtension);
            Directory.CreateDirectory(Path.GetDirectoryName(newImageUri.LocalPath)!);
            File.Move(existingImageUri.LocalPath, newImageUri.LocalPath);
            character.Image = newImageUri.LocalPath;
        }

        _settings.Characters.Remove(internalName.Id);
        character.InternalName = newInternalName.Id;
        _settings.Characters[newInternalName.Id] = character;

        await SaveSettingsAsync().ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<JsonUiCategory>> GetUiCategoriesAsync()
    {
        var settings = await ReadSettingsAsync().ConfigureAwait(false);
        return settings.UiCategories.ToList();
    }

    internal async Task<JsonUiCategory> CreateUiCategoryAsync(string name, Uri? image)
    {
        var settings = await ReadSettingsAsync().ConfigureAwait(false);

        var category = new JsonUiCategory
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Order = settings.UiCategories.Count == 0 ? 0 : settings.UiCategories.Max(c => c.Order) + 1
        };

        if (image is not null)
        {
            var imageFile = new FileInfo(image.LocalPath);
            if (!imageFile.Exists)
                throw new InvalidOperationException("Image file does not exist");

            var destinationPath = CreateUiCategoryImagePath(category.Id, imageFile.Extension);
            imageFile.CopyTo(destinationPath.LocalPath, true);
            category.Image = destinationPath.LocalPath;
        }

        settings.UiCategories.Add(category);
        await SaveSettingsAsync().ConfigureAwait(false);

        return category;
    }

    internal async Task<JsonUiCategory> UpdateUiCategoryAsync(Guid categoryId, UpdateUiCategoryRequest request)
    {
        if (!request.AnyValuesSet)
            throw new InvalidOperationException("No values were set to update");

        var settings = await ReadSettingsAsync().ConfigureAwait(false);

        var category = settings.UiCategories.FirstOrDefault(c => c.Id == categoryId);
        if (category is null)
        {
            if (categoryId == Guid.Empty)
            {
                category = new JsonUiCategory
                {
                    Id = Guid.Empty,
                    Name = "全部",
                    Order = int.MinValue
                };
                settings.UiCategories.Add(category);
            }
            else
            {
                throw new InvalidOperationException("Category not found");
            }
        }

        if (request.Name.IsSet)
        {
            var name = (request.Name.ValueToSet ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Category name must not be empty");
            category.Name = name;
        }

        if (request.Image.IsSet)
        {
            if (!category.Image.IsNullOrEmpty() && File.Exists(category.Image))
                File.Delete(category.Image);

            category.Image = null;

            if (request.Image.ValueToSet is not null)
            {
                var imageFile = new FileInfo(request.Image.ValueToSet.LocalPath);
                if (!imageFile.Exists)
                    throw new InvalidOperationException("Image file does not exist");

                var destinationPath = CreateUiCategoryImagePath(category.Id, imageFile.Extension);
                imageFile.CopyTo(destinationPath.LocalPath, true);
                category.Image = destinationPath.LocalPath;
            }
        }

        if (request.IsHidden.IsSet)
            category.IsHidden = request.IsHidden.ValueToSet;

        await SaveSettingsAsync().ConfigureAwait(false);
        return category;
    }

    internal async Task DeleteUiCategoryAsync(Guid categoryId)
    {
        var settings = await ReadSettingsAsync().ConfigureAwait(false);

        var category = settings.UiCategories.FirstOrDefault(c => c.Id == categoryId);
        if (category is null)
            return;

        settings.UiCategories.Remove(category);

        if (!category.Image.IsNullOrEmpty() && File.Exists(category.Image))
            File.Delete(category.Image);

        foreach (var (internalName, tags) in settings.CharacterTags.ToArray())
        {
            var remaining = tags.Where(t => !t.Equals(categoryId.ToString(), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (remaining.Length == 0)
                settings.CharacterTags.Remove(internalName);
            else
                settings.CharacterTags[internalName] = remaining;
        }

        await SaveSettingsAsync().ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<Guid>> GetCharacterTagsAsync(InternalName internalName)
    {
        var settings = await ReadSettingsAsync().ConfigureAwait(false);
        if (!settings.CharacterTags.TryGetValue(internalName.Id, out var tags))
            return Array.Empty<Guid>();

        var result = new List<Guid>();
        foreach (var tag in tags)
        {
            if (Guid.TryParse(tag, out var id))
                result.Add(id);
        }

        return result;
    }

    internal async Task SetCharacterTagsAsync(InternalName internalName, IEnumerable<Guid> tags)
    {
        var settings = await ReadSettingsAsync().ConfigureAwait(false);
        var tagStrings = tags.Select(t => t.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (tagStrings.Length == 0)
            settings.CharacterTags.Remove(internalName.Id);
        else
            settings.CharacterTags[internalName.Id] = tagStrings;

        await SaveSettingsAsync().ConfigureAwait(false);
    }

    internal Uri CreateCharacterImagePath(InternalName internalName, string fileExtension)
        => new(Path.Combine(CharacterImageFolder.FullName, $"{internalName.Id}{fileExtension}"));

    internal Uri CreateUiCategoryImagePath(Guid categoryId, string fileExtension)
        => new(Path.Combine(UiCategoryImageFolder.FullName, $"{categoryId}{fileExtension}"));

    internal Uri? GetCharacterImagePath(InternalName internalName)
    {
        if (_settings is null)
            return null;

        if (!_settings.Characters.TryGetValue(internalName.Id, out var character))
            return null;

        return ParseAbsoluteFilePath(character.Image);
    }

    internal async Task SetCharacterEnabledAsync(InternalName internalName, bool enabled)
    {
        await ReadSettingsAsync().ConfigureAwait(false);

        if (!_settings.Characters.TryGetValue(internalName.Id, out var character))
            throw new InvalidOperationException("Character not found. Try restarting UMManager");

        character.IsEnabled = enabled;
        await SaveSettingsAsync().ConfigureAwait(false);
    }

    internal async Task<Uri?> SetCharacterImageAsync(InternalName internalName, Uri? newImage)
    {
        await ReadSettingsAsync().ConfigureAwait(false);

        if (!_settings.Characters.TryGetValue(internalName.Id, out var character))
            throw new InvalidOperationException("Character not found. Try restarting UMManager");

        var existingImagePath = ParseAbsoluteFilePath(character.Image);
        if (existingImagePath is not null &&
            IsInFolder(existingImagePath.LocalPath, CharacterImageFolder.FullName) &&
            File.Exists(existingImagePath.LocalPath))
        {
            File.Delete(existingImagePath.LocalPath);
        }

        character.Image = null;

        if (newImage is null)
        {
            await SaveSettingsAsync().ConfigureAwait(false);
            return null;
        }

        if (!newImage.IsFile || !File.Exists(newImage.LocalPath))
            throw new InvalidOperationException("Image file does not exist");

        var fileExtension = Path.GetExtension(newImage.LocalPath);
        if (fileExtension.IsNullOrEmpty())
            throw new InvalidOperationException("Image file must have an extension");

        var destinationPath = CreateCharacterImagePath(internalName, fileExtension);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath.LocalPath)!);
        File.Copy(newImage.LocalPath, destinationPath.LocalPath, true);
        character.Image = destinationPath.LocalPath;

        await SaveSettingsAsync().ConfigureAwait(false);
        return destinationPath;
    }

    internal async Task DeleteCharacterAsync(InternalName internalName)
    {
        await ReadSettingsAsync(useCache: false).ConfigureAwait(false);

        if (!_settings.Characters.Remove(internalName.Id, out var character))
            throw new InvalidOperationException("Character to delete not found. Try restarting UMManager");

        if (_settings.CharacterTags.ContainsKey(internalName.Id))
            _settings.CharacterTags.Remove(internalName.Id);

        var image = ParseAbsoluteFilePath(character.Image);
        if (image is not null &&
            IsInFolder(image.LocalPath, CharacterImageFolder.FullName) &&
            File.Exists(image.LocalPath))
        {
            File.Delete(image.LocalPath);
        }

        await SaveSettingsAsync().ConfigureAwait(false);
    }

    private Uri? ParseAbsoluteFilePath(string? path)
    {
        if (path.IsNullOrEmpty())
            return null;

        return Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile ? uri : null;
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
}

internal sealed class GameServiceRootV2
{
    internal const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Dictionary<string, JsonCharacterRecord> Characters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<JsonUiCategory> UiCategories { get; set; } = new();

    public Dictionary<string, string[]> CharacterTags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class JsonCharacterRecord
{
    public string InternalName { get; set; } = string.Empty;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? DisplayName { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string[]? Keys { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? ReleaseDate { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Image { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int? Rarity { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Element { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Class { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string[]? Region { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsMultiMod { get; set; }

    public bool IsEnabled { get; set; } = true;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public JsonCharacterSkin[]? InGameSkins { get; set; }
}
internal sealed class JsonUiCategory
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Image { get; set; }

    public int Order { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsHidden { get; set; }
}
