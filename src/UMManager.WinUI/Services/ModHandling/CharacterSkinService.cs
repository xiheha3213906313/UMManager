using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using UMManager.Core.Contracts.Entities;
using UMManager.Core.Contracts.Services;
using UMManager.Core.Entities;
using UMManager.Core.GamesService;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.Helpers;
using UMManager.Core.Services;
using Serilog;

namespace UMManager.WinUI.Services.ModHandling;

public class CharacterSkinService
{
    private readonly ILogger _logger;
    private readonly ModCrawlerService _modCrawlerService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly IGameService _gameService;

    public CharacterSkinService(ModCrawlerService modCrawlerService, ISkinManagerService skinManagerService,
        ILogger logger, IGameService gameService)
    {
        _modCrawlerService = modCrawlerService;
        _skinManagerService = skinManagerService;
        _gameService = gameService;
        _logger = logger.ForContext<CharacterSkinService>();
    }

    private static readonly JsonSerializerOptions SkinFolderJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record SkinFolderMetadata
    {
        public string SkinInternalName { get; init; } = string.Empty;
    }

    private static string? TryGetSkinInternalNameFromFolder(ISkinMod mod)
    {
        var skinFolder = Directory.GetParent(mod.FullPath);
        if (skinFolder is null)
            return null;

        var metadataPath = Path.Combine(skinFolder.FullName, Constants.SkinFolderMetadataFileName);
        if (!File.Exists(metadataPath))
            return null;

        try
        {
            var metadata = JsonSerializer.Deserialize<SkinFolderMetadata>(File.ReadAllText(metadataPath), SkinFolderJsonOptions);
            return metadata?.SkinInternalName;
        }
        catch
        {
            return null;
        }
    }

    public async IAsyncEnumerable<ISkinMod> FilterModsToSkinAsync(ICharacterSkin skin,
        IEnumerable<ISkinMod> mods, bool useSettingsCache = false, bool ignoreUndetectableMods = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var mod in mods)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folderSkinInternalName = TryGetSkinInternalNameFromFolder(mod);
            if (!folderSkinInternalName.IsNullOrEmpty())
            {
                if (skin.InternalNameEquals(folderSkinInternalName))
                    yield return mod;
                continue;
            }

            var modSettings = await mod.Settings.TryReadSettingsAsync(useCache: useSettingsCache, cancellationToken: cancellationToken);

            if (modSettings is null)
            {
                _logger.Warning("Could not read settings for mod {mod}", mod.Name);
                Debugger.Break();

                if (ignoreUndetectableMods)
                    continue;
            }


            var modSkin = modSettings?.CharacterSkinOverride;

            // Has skin override and is a match for the shown skin
            if (modSkin is not null && skin.InternalNameEquals(modSkin))
            {
                yield return mod;
                continue;
            }

            // Has override skin, but does not match any of the characters skins
            if (modSkin is not null && !skin.Character.Skins.Any(skinVm =>
                    skinVm.InternalNameEquals(modSkin)))
            {
                // In this case, the override skin is not a valid skin for this character, so we just add it.
                yield return mod;
                continue;
            }

            ICharacterSkin? detectedSkin;
            try
            {
                detectedSkin =
                    _modCrawlerService.GetFirstSubSkinRecursive(mod.FullPath, skin.Character.InternalName,
                        cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }


            // If we can detect the skin, and the mod has no override skin, check if the detected skin matches the shown skin.
            if (modSkin is null && detectedSkin is not null && detectedSkin.InternalNameEquals(skin.InternalName))
            {
                yield return mod;
                continue;
            }

            // If we can't detect the skin, and the mod has no override skin.
            // We add it if the caller wants to see undetectable mods.
            if (detectedSkin is null && modSkin is null && !ignoreUndetectableMods)
                yield return mod;
        }
    }


    public async IAsyncEnumerable<ISkinMod> GetModsForSkinAsync(ICharacterSkin skin,
        bool ignoreUndetectableMods = false)
    {
        var modList = _skinManagerService.GetCharacterModList(skin.Character);

        var mods = modList.Mods.Select(entry => entry.Mod).ToArray();
        await foreach (var skinMod in FilterModsToSkinAsync(skin, mods, ignoreUndetectableMods))
            yield return skinMod;
    }

    public async IAsyncEnumerable<CharacterSkinEntry> GetCharacterSkinEntriesForSkinAsync(ICharacterSkin skin, bool useSettingsCache = false,
        bool ignoreUndetectableMods = false, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var modList = _skinManagerService.GetCharacterModList(skin.Character);

        var mods = modList.Mods.ToArray();
        await foreach (var skinMod in FilterModsToSkinAsync(skin, mods.Select(ske => ske.Mod),
                           ignoreUndetectableMods: ignoreUndetectableMods, useSettingsCache: useSettingsCache,
                           cancellationToken: cancellationToken).ConfigureAwait(false))
        {

            yield return mods.First(m => m.Id == skinMod.Id);
        }

    }


    public async Task<GetAllModsBySkinResult?> GetAllModsBySkinAsync(ICharacter character)
    {
        var modList = _skinManagerService.GetCharacterModListOrDefault(character.InternalName);
        if (modList is null) return null;

        var mods = modList.Mods.Select(entry => entry.Mod).ToArray();

        var result = new Dictionary<ICharacterSkin, List<ISkinMod>>();
        foreach (var skin in character.Skins)
        {
            var modsForSkin = new List<ISkinMod>();
            await foreach (var skinMod in FilterModsToSkinAsync(skin, mods))
            {
                modsForSkin.Add(skinMod);
            }

            result.Add(skin, modsForSkin.ToList());
        }

        var unknownMods = mods
            .Where(mod => !result.Values.SelectMany(detectedMods => detectedMods)
                .Contains(mod))
            .ToList();

        return new GetAllModsBySkinResult(result, unknownMods);
    }


    public async Task<ICharacterSkin?> GetFirstSkinForModAsync(ISkinMod mod, ICharacter? character = null)
    {
        var folderSkinInternalName = TryGetSkinInternalNameFromFolder(mod);
        if (!folderSkinInternalName.IsNullOrEmpty())
        {
            var found = character?.Skins.FirstOrDefault(s => s.InternalNameEquals(folderSkinInternalName));
            if (found is not null)
                return found;

            var allSkins = _gameService.GetCharacters().SelectMany(c => c.Skins).ToArray();
            return allSkins.FirstOrDefault(s => s.InternalNameEquals(folderSkinInternalName));
        }

        var modSettings = await mod.Settings.TryReadSettingsAsync();

        if (modSettings is null)
        {
            _logger.Warning("Could not read settings for mod {mod}", mod.Name);
            Debugger.Break();
        }

        var modSkin = modSettings?.CharacterSkinOverride;


        if (!modSkin.IsNullOrEmpty())
        {
            var foundSkin = character?.Skins.FirstOrDefault(skinVm =>
                skinVm.InternalNameEquals(modSkin));

            if (foundSkin != null)
                return foundSkin;


            var allSkins = _gameService.GetCharacters().SelectMany(characterVm => characterVm.Skins).ToArray();

            var skin = allSkins.FirstOrDefault(skinVm =>
                skinVm.InternalNameEquals(modSkin));

            if (skin is not null)
                return skin;
        }

        var detectedSkin =
            _modCrawlerService.GetFirstSubSkinRecursive(mod.FullPath, character?.InternalName.Id);

        return detectedSkin;
    }

    public record GetAllModsBySkinResult(
        Dictionary<ICharacterSkin, List<ISkinMod>> ModsBySkin,
        List<ISkinMod> UndetectableMods);
}
