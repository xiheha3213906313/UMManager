﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿#if DEBUG
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
#endif
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UMManager.Core.Contracts.Entities;
using UMManager.Core.Contracts.Services;
using UMManager.Core.Entities;
using UMManager.Core.Entities.Mods.SkinMod;
using UMManager.Core.GamesService;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.GamesService.Models;
using UMManager.Core.Helpers;
using OneOf;
using OneOf.Types;
using Serilog;
using static UMManager.Core.Contracts.Services.RefreshResult;

namespace UMManager.Core.Services;

public sealed class SkinManagerService : ISkinManagerService
{
    private readonly IGameService _gameService;
    private readonly ILogger _logger;
    private readonly ModCrawlerService _modCrawlerService;
    private static readonly JsonSerializerOptions _skinFolderJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private DirectoryInfo _unloadedModsFolder = null!;
    private DirectoryInfo _activeModsFolder = null!;
    private DirectoryInfo? _threeMigotoFolder;

    private FileSystemWatcher _userIniWatcher = null!;

    private readonly List<ICharacterModList> _characterModLists = new();

    public string ThreeMigotoRootfolder => _threeMigotoFolder?.FullName ?? string.Empty;

    public IReadOnlyCollection<ICharacterModList> CharacterModLists
    {
        get
        {
            lock (_modListLock)
            {
                return _characterModLists.AsReadOnly();
            }
        }
    }


    private readonly object _modListLock = new();
    public bool IsInitialized { get; private set; }

    public SkinManagerService(IGameService gameService, ILogger logger, ModCrawlerService modCrawlerService)
    {
        _gameService = gameService;
        _modCrawlerService = modCrawlerService;
        _logger = logger.ForContext<SkinManagerService>();
    }

    public string UnloadedModsFolderPath => _unloadedModsFolder.FullName;
    public string ActiveModsFolderPath => _activeModsFolder.FullName;

    public bool UnloadingModsEnabled { get; private set; }


    private void AddNewModList(ICharacterModList newModList)
    {
        lock (_modListLock)
        {
            if (_characterModLists.Any(x => x.Character.InternalNameEquals(newModList.Character.InternalName)))
                throw new InvalidOperationException(
                    $"Mod list for character '{newModList.Character.DisplayName}' already exists");

            _characterModLists.Add(newModList);
        }
    }

    public async Task ScanForModsAsync()
    {
        _activeModsFolder.Refresh();

        var characters = _gameService.GetAllModdableObjects();

        var modsFound = new ConcurrentDictionary<Guid, ISkinMod>();


        await Parallel.ForEachAsync(characters, async (modObject, _) =>
        {
            var modObjectFolder = new DirectoryInfo(GetCharacterModFolderPath(modObject));

            var characterModList = new CharacterModList(modObject, modObjectFolder.FullName, logger: _logger);
            AddNewModList(characterModList);

            if (!modObjectFolder.Exists)
            {
                _logger.Verbose("ModdableObject folder for '{Character}' does not exist", modObject.DisplayName);
                return;
            }

            foreach (var modFolder in EnumerateModFolders(modObjectFolder))
            {
                try
                {
                    var mod = await CreateModAsync(modFolder).ConfigureAwait(false);

                    if (!modsFound.TryAdd(mod.Id, mod))
                    {
                        mod = await SkinMod.CreateModAsync(modFolder.FullName, true).ConfigureAwait(false);


                        // If this fails, the mod will be skipped, very unlikely that a duplicate ID will be generated
                        if (!modsFound.TryAdd(mod.Id, mod))
                        {
                            _logger.Error("Failed to generate new ID for mod '{ModName}', UMManager will not track this mod", mod.FullPath);
                            continue;
                        }
                    }


                    characterModList.TrackMod(mod);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to initialize mod '{ModFolder}'", modFolder.FullName);
                }
            }
        }).ConfigureAwait(false);
    }

    private static IEnumerable<DirectoryInfo> EnumerateModFolders(DirectoryInfo characterFolder)
    {
        foreach (var skinFolder in characterFolder.EnumerateDirectories())
        {
            foreach (var modFolder in skinFolder.EnumerateDirectories())
                yield return modFolder;
        }
    }

    private async Task<ISkinMod> CreateModAsync(DirectoryInfo modFolder)
    {
        try
        {
            return await SkinMod.CreateModAsync(modFolder.FullName).ConfigureAwait(false);
        }
        catch (JsonException e)
        {
            modFolder.Refresh();

            var invalidJasmConfigFile =
                modFolder.EnumerateFiles(Constants.ModConfigFileName).FirstOrDefault() ??
                modFolder.EnumerateFiles(Constants.LegacyModConfigFileName).FirstOrDefault();
            if (invalidJasmConfigFile is null)
                throw new FileNotFoundException("Could not find mod config file", Constants.ModConfigFileName, e);


            _logger.Error(e, "Failed to initialize mod due to invalid config file'{ModFolder}'",
                modFolder.FullName);
            _logger.Information("Renaming invalid config file '{ConfigFile}' to {ConfigFile}.invalid",
                invalidJasmConfigFile.FullName, invalidJasmConfigFile.FullName);
            invalidJasmConfigFile.MoveTo(Path.Combine(modFolder.FullName,
                invalidJasmConfigFile.Name + ".invalid"));

            return await SkinMod.CreateModAsync(modFolder.FullName).ConfigureAwait(false);
        }
    }

    public async Task<RefreshResult> RefreshModsAsync(string? refreshForCharacter = null, CancellationToken ct = default)
    {
        var modsUntracked = new List<string>();
        var newModsFound = new List<ISkinMod>();
        var duplicateModsFound = new List<DuplicateMods>();
        var errors = new List<string>();

        foreach (var characterModList in _characterModLists)
        {
            ct.ThrowIfCancellationRequested();
            if (refreshForCharacter is not null &&
                !characterModList.Character.InternalNameEquals(refreshForCharacter)) continue;

            var modsDirectory = new DirectoryInfo(characterModList.AbsModsFolderPath);
            modsDirectory.Refresh();

            if (!modsDirectory.Exists)
            {
                _logger.Debug("RefreshModsAsync() Character mod folder for '{Character}' does not exist",
                    characterModList.Character.DisplayName);

                if (characterModList.IsCharacterFolderCreated())
                    throw new InvalidOperationException(
                        $"Character mod folder for '{characterModList.Character.DisplayName}' does not exist, but the character folder is created");

                continue;
            }

            var orphanedMods = new List<CharacterSkinEntry>(characterModList.Mods);

            foreach (var modDirectory in EnumerateModFolders(modsDirectory))
            {
                CharacterSkinEntry? mod = null;
                var modParentFolder = modDirectory.Parent;
                if (modParentFolder is null)
                    continue;

                foreach (var x in characterModList.Mods)
                {
                    if (x.Mod.FullPath.AbsPathCompare(modDirectory.FullName)
                        &&
                        Directory.Exists(Path.Combine(modParentFolder.FullName,
                            ModFolderHelpers.GetFolderNameWithDisabledPrefix(modDirectory.Name)))
                        &&
                        Directory.Exists(Path.Combine(modParentFolder.FullName,
                            ModFolderHelpers.GetFolderNameWithoutDisabledPrefix(modDirectory.Name)))
                       )
                    {
                        var newName = modDirectory.Name;

                        while (Directory.Exists(Path.Combine(modParentFolder.FullName, newName)))
                            newName = DuplicateModAffixHelper.AppendNumberAffix(newName);

                        _logger.Warning(
                            "Mod '{ModName}' has both enabled and disabled folders, renaming folder",
                            modDirectory.Name);

                        duplicateModsFound.Add(new DuplicateMods(x.Mod.Name, newName));
                        x.Mod.Rename(newName);
                        mod = x;
                        mod.Mod.ClearCache();
                        orphanedMods.Remove(x);
                        await TryRefreshIniPathAsync(mod.Mod, errors).ConfigureAwait(false);
                        break;
                    }

                    if (x.Mod.FullPath.AbsPathCompare(modDirectory.FullName))
                    {
                        mod = x;
                        mod.Mod.ClearCache();
                        orphanedMods.Remove(x);
                        await TryRefreshIniPathAsync(mod.Mod, errors).ConfigureAwait(false);
                        break;
                    }

                    var disabledName = ModFolderHelpers.GetFolderNameWithDisabledPrefix(modDirectory.Name);
                    if (x.Mod.FullPath.AbsPathCompare(Path.Combine(modParentFolder.FullName, disabledName)))
                    {
                        mod = x;
                        mod.Mod.ClearCache();
                        orphanedMods.Remove(x);
                        await TryRefreshIniPathAsync(mod.Mod, errors).ConfigureAwait(false);
                        break;
                    }
                }

                if (mod is not null) continue;

                try
                {
                    var newMod = await SkinMod.CreateModAsync(modDirectory.FullName).ConfigureAwait(false);

                    if (GetModById(newMod.Id) is not null)
                    {
                        _logger.Debug("Mod '{ModName}' has ID that already exists in mod list, generating new ID",
                            newMod.Name);
                        newMod = await SkinMod.CreateModAsync(modDirectory.FullName, true).ConfigureAwait(false);
                    }


                    characterModList.TrackMod(newMod);
                    newModsFound.Add(newMod);
                    _logger.Debug("Found new mod '{ModName}' in '{CharacterFolder}'", newMod.Name,
                        characterModList.Character.DisplayName);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to create mod from folder '{ModFolder}'", modDirectory.FullName);
                    errors.Add(
                        $"Failed to track new mod folder: '{modDirectory.FullName}' | For character {characterModList.Character.DisplayName}");
                }
            }

            orphanedMods.ForEach(x =>
            {
                characterModList.UnTrackMod(x.Mod);
                modsUntracked.Add(x.Mod.FullPath);
                _logger.Debug("Mod '{ModName}' in '{CharacterFolder}' is no longer tracked", x.Mod.Name,
                    characterModList.Character.DisplayName);
            });
            continue;

            async Task TryRefreshIniPathAsync(ISkinMod mod, IList<string> errorList)
            {
                try
                {
                    await mod.GetModIniPathAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
#if DEBUG
                    throw;
#endif
                    _logger.Error(e, "Failed getting mod .ini path when refreshing mods");
                    errorList.Add(
                        $"Failed to get ini path for mod: '{mod.GetDisplayName()}' | Mod file path: {mod.FullPath}");
                }
            }
        }

        return new RefreshResult(modsUntracked, newModsFound, duplicateModsFound, errors: errors);
    }


    public async Task<OneOf<Success, Error<string>[]>> TransferMods(ICharacterModList source,
        ICharacterModList destination,
        IEnumerable<Guid> modsEntryIds)
    {
        var mods = source.Mods.Where(x => modsEntryIds.Contains(x.Id)).Select(x => x.Mod).ToList();
        foreach (var mod in mods)
        {
            if (!source.Mods.Select(modEntry => modEntry.Mod).Contains(mod))
                throw new InvalidOperationException(
                    $"Mod {mod.Name} is not in source character mod list {source.Character.DisplayName}");


            if (mods.Select(x => x.Name).Any(destination.FolderAlreadyExists))
                throw new InvalidOperationException(
                    $"Mod {mod.Name} already exists in destination character mod list {destination.Character.DisplayName}");
        }

        _logger.Information("Transferring {ModsCount} mods from '{SourceCharacter}' to '{DestinationCharacter}'",
            mods.Count, source.Character.InternalName, destination.Character.InternalName);

        using var sourceDisabled = source.DisableWatcher();
        using var destinationDisabled = destination.DisableWatcher();

        var destinationRoot = GetOrCreateDefaultSkinFolderPath(destination.Character);

        var errors = new List<Error<string>>();
        foreach (var mod in mods)
        {
            source.UnTrackMod(mod);
            mod.MoveTo(destinationRoot);
            destination.TrackMod(mod);

            try
            {
                var skinSettings = await mod.Settings.ReadSettingsAsync().ConfigureAwait(false);
                skinSettings.CharacterSkinOverride = null;
                await mod.Settings.SaveSettingsAsync(skinSettings).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to clear skin override for mod '{ModName}'", mod.Name);
                errors.Add(new Error<string>(
                    $"Failed to clear skin override for mod '{mod.Name}'. Reason: {e.Message}"));
            }
        }

        return errors.Any() ? errors.ToArray() : new Success();
    }

    public event EventHandler<ExportProgress>? ModExportProgress;

    public ISkinMod? GetModById(Guid id) =>
        CharacterModLists.SelectMany(x => x.Mods).FirstOrDefault(x => x.Id == id)?.Mod;

    public CharacterSkinEntry? GetModEntryById(Guid id)
    {
        var modEntries = GetAllMods(GetOptions.All);
        return modEntries.FirstOrDefault(x => x.Id == id);
    }

    public Task<bool> IsModListEnabledAsync(IModdableObject moddableObject)
    {
        var modList = GetCharacterModListOrDefault(moddableObject.InternalName);
        return Task.FromResult(modList is not null);
    }

    public Task EnableModListAsync(IModdableObject moddableObject)
    {
        var enabledPath = GetCharacterModFolderPath(moddableObject);
        var disabledPath = GetCharacterModsFolderPath(moddableObject, disabledPrefix: true);

        if (Directory.Exists(disabledPath) && !Directory.Exists(enabledPath))
        {
            try
            {
                Directory.Move(disabledPath, enabledPath);
                _logger.Information("Renamed character folder '{OldFolder}' -> '{NewFolder}'",
                    new DirectoryInfo(disabledPath).Name, new DirectoryInfo(enabledPath).Name);
            }
            catch (Exception e)
            {
                _logger.Warning(e, "Failed to rename disabled character folder '{OldFolder}' -> '{NewFolder}'",
                    disabledPath, enabledPath);
            }
        }

        var modList = new CharacterModList(moddableObject, GetCharacterModFolderPath(moddableObject), logger: _logger);

        AddNewModList(modList);

        return RefreshModsAsync(moddableObject.InternalName);
    }

    public Task DisableModListAsync(IModdableObject moddableObject, bool deleteFolder = false)
    {
        var modList = GetCharacterModList(moddableObject);
        var modFolder = new DirectoryInfo(modList.AbsModsFolderPath);
        lock (_modListLock)
        {
            _characterModLists.Remove(modList);
            modList.Dispose();
        }

        if (deleteFolder && modFolder.Exists)
        {
            _logger.Information("Deleting mod folder '{ModFolder}'", modFolder.FullName);
            modFolder.Delete(true);
            return Task.CompletedTask;
        }

        if (modFolder.Exists && !modFolder.Name.StartsWith(ModFolderHelpers.DISABLED_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            var disabledFolderPath = Path.Combine(modFolder.Parent?.FullName ?? _activeModsFolder.FullName,
                ModFolderHelpers.DISABLED_PREFIX + modFolder.Name);

            if (!Directory.Exists(disabledFolderPath))
            {
                try
                {
                    Directory.Move(modFolder.FullName, disabledFolderPath);
                    _logger.Information("Renamed character folder '{OldFolder}' -> '{NewFolder}'",
                        modFolder.Name, new DirectoryInfo(disabledFolderPath).Name);
                }
                catch (Exception e)
                {
                    _logger.Warning(e, "Failed to rename character folder '{OldFolder}' -> '{NewFolder}'",
                        modFolder.FullName, disabledFolderPath);
                }
            }
        }

        return Task.CompletedTask;
    }

    public ISkinMod AddMod(ISkinMod mod, ICharacterModList modList, bool move = false, InternalName? skinInternalName = null, string? skinDisplayName = null)
    {
        if (GetModById(mod.Id) is not null)
            throw new InvalidOperationException($"Mod with id {mod.Id} is already tracked in a modList");

        modList.InstantiateCharacterFolder();
        var destinationFolder = skinInternalName is null || (modList.Character is ICharacter character &&
                                                             character.Skins.FirstOrDefault(s => s.InternalNameEquals(skinInternalName)) is { IsDefault: true })
            ? GetOrCreateDefaultSkinFolderPath(modList.Character)
            : GetOrCreateSkinFolderPath(modList.Character, skinInternalName.Id, skinDisplayName ?? skinInternalName.Id);

        foreach (var existingFolder in new DirectoryInfo(destinationFolder).EnumerateDirectories())
        {
            if (ModFolderHelpers.FolderNameEquals(mod.Name, existingFolder.Name))
                throw new InvalidOperationException(
                    $"Mod with name {mod.Name} already exists in skin folder for {modList.Character.DisplayName}");
        }

        using var disableWatcher = modList.DisableWatcher();
        if (move)
            mod.MoveTo(destinationFolder);
        else
            mod = mod.CopyTo(destinationFolder);

        modList.TrackMod(mod);
        return mod;
    }

    public void ExportMods(ICollection<ICharacterModList> characterModLists, string exportPath,
        bool removeLocalJasmSettings = true, bool zip = true, bool keepCharacterFolderStructure = false,
        SetModStatus setModStatus = SetModStatus.KeepCurrent)
    {
        if (characterModLists.Count == 0)
            throw new ArgumentException("Value cannot be an empty collection.", nameof(characterModLists));
        ArgumentNullException.ThrowIfNull(exportPath);

        var exportFolderResultName = $"UMManager_MOD_EXPORT_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";

        var exportFolder = new DirectoryInfo(Path.Combine(exportPath, exportFolderResultName));
        exportFolder.Create();

        if (exportFolder.EnumerateFileSystemInfos().Any())
            throw new InvalidOperationException("Export folder is not empty");


        var modsToExport = new List<CharacterSkinEntry>();


        foreach (var characterModList in characterModLists) modsToExport.AddRange(characterModList.Mods);

        double modsProgress = 0;
        double divider = modsToExport.Count + (removeLocalJasmSettings ? 1 : 0) +
                         (setModStatus != SetModStatus.KeepCurrent ? 1 : 0);
        var modsProgressIncrement = 100 / divider;

        if (!keepCharacterFolderStructure && !zip) // Copy mods unorganized
        {
            var exportedMods = new List<IMod>();
            foreach (var characterSkinEntry in modsToExport)
            {
                var mod = characterSkinEntry.Mod;
                ModExportProgress?.Invoke(this,
                    new ExportProgress(modsProgress += modsProgressIncrement, mod.Name, "Copying Folders"));

                if (CheckForDuplicates(exportFolder, mod)) // Handle duplicate mod names
                {
                    _logger.Information(
                        "Mod '{ModName}' already exists in export folder, appending GUID to folder name",
                        characterSkinEntry.Mod.Name);
                    var oldName = mod.Name;
                    using var disableWatcher = characterSkinEntry.ModList.DisableWatcher();
                    mod.Rename(mod.Name + "__" + Guid.NewGuid().ToString("N"));
                    exportedMods.Add(mod.CopyTo(exportFolder.FullName));
                    mod.Rename(oldName);
                    _logger.Information("Copied mod '{ModName}' to export folder", mod.Name);
                    continue;
                }

                exportedMods.Add(characterSkinEntry.Mod.CopyTo(exportFolder.FullName));
                _logger.Information("Copied mod '{ModName}' to export folder", mod.Name);
            }

            ModExportProgress?.Invoke(this,
                new ExportProgress(modsProgress += modsProgressIncrement, null, "Removing local settings..."));
            RemoveJASMSettings(removeLocalJasmSettings, exportedMods);

            ModExportProgress?.Invoke(this,
                new ExportProgress(modsProgress += modsProgressIncrement, null, "Setting Mod Status..."));
            SetModsStatus(setModStatus, exportedMods);

            ModExportProgress?.Invoke(this,
                new ExportProgress(100, null, "Finished"));
            return;
        }

        if (keepCharacterFolderStructure && !zip) // Copy mods organized by character
        {
            var characterToFolder = new Dictionary<IModdableObject, DirectoryInfo>();
            var emptyFoldersCount = 0;

            foreach (var characterModList in characterModLists)
            {
                if (!characterModList.Mods.Any())
                {
                    emptyFoldersCount++;
                    continue; // Skip empty character folders
                }

                var characterFolder = new DirectoryInfo(Path.Combine(exportFolder.FullName,
                    characterModList.Character.ModCategory.InternalName, new DirectoryInfo(characterModList.AbsModsFolderPath).Name));

                characterToFolder.Add(characterModList.Character, characterFolder);
                characterFolder.Create();
            }

            if (characterToFolder.Count != _gameService.GetAllModdableObjects().Count - emptyFoldersCount)
                throw new InvalidOperationException(
                    "Failed to create character folders in export folder, character mismatch");

            var exportedMods = new List<IMod>();
            foreach (var characterSkinEntry in modsToExport)
            {
                var mod = characterSkinEntry.Mod;
                var characterFolder = characterToFolder[characterSkinEntry.ModList.Character];
                var skinFolderName = new DirectoryInfo(mod.FullPath).Parent?.Name ?? GetDefaultSkinFolderDisplayName();
                var destinationFolder = new DirectoryInfo(Path.Combine(characterFolder.FullName, skinFolderName));
                destinationFolder.Create();
                ModExportProgress?.Invoke(this,
                    new ExportProgress(modsProgress += modsProgressIncrement, mod.Name, "Copying Folders"));

                if (CheckForDuplicates(destinationFolder, mod)) // Handle duplicate mod names
                {
                    _logger.Information(
                        "Mod '{ModName}' already exists in export folder, appending GUID to folder name",
                        characterSkinEntry.Mod.Name);

                    var oldName = mod.Name;
                    using var disableWatcher = characterSkinEntry.ModList.DisableWatcher();
                    mod.Rename(mod.Name + "__" + Guid.NewGuid().ToString("N"));
                    exportedMods.Add(mod.CopyTo(destinationFolder.FullName));
                    mod.Rename(oldName);
                    _logger.Information("Copied mod '{ModName}' to export character folder '{CharacterFolder}'",
                        mod.Name,
                        characterSkinEntry.ModList.Character.InternalName);

                    continue;
                }

                exportedMods.Add(characterSkinEntry.Mod.CopyTo(destinationFolder.FullName));
                _logger.Information("Copied mod '{ModName}' to export character folder '{CharacterFolder}'", mod.Name,
                    characterSkinEntry.ModList.Character.InternalName);
            }

            ModExportProgress?.Invoke(this,
                new ExportProgress(modsProgress += modsProgressIncrement, null, "Removing local settings..."));
            RemoveJASMSettings(removeLocalJasmSettings, exportedMods);

            ModExportProgress?.Invoke(this,
                new ExportProgress(modsProgress += modsProgressIncrement, null, "Setting Mod Status..."));
            SetModsStatus(setModStatus, exportedMods);

            ModExportProgress?.Invoke(this,
                new ExportProgress(100, null, "Finished"));


            return;
        }

        if (zip)
            throw new NotImplementedException();
    }

    private static bool CheckForDuplicates(DirectoryInfo destinationFolder, IMod mod)
    {
        destinationFolder.Refresh();
        foreach (var directory in destinationFolder.EnumerateDirectories())
        {
            if (directory.Name.Equals(mod.Name, StringComparison.CurrentCultureIgnoreCase))
                return true;

            if (directory.Name.EndsWith(mod.Name, StringComparison.CurrentCultureIgnoreCase))
                return true;

            if (mod.Name.Replace(CharacterModList.DISABLED_PREFIX, "") == directory.Name ||
                mod.Name.Replace("DISABLED", "") == directory.Name)
                return true;
        }

        return false;
    }

    private static void SetModsStatus(SetModStatus setModStatus, IEnumerable<IMod> mods)
    {
        switch (setModStatus)
        {
            case SetModStatus.EnableAllMods:
                {
                    foreach (var mod in mods)
                    {
                        var enabledName = mod.Name;
                        enabledName = enabledName.Replace(CharacterModList.DISABLED_PREFIX, "");
                        enabledName = enabledName.Replace("DISABLED", "");
                        if (enabledName != mod.Name)
                            mod.Rename(enabledName);
                    }

                    break;
                }
            case SetModStatus.DisableAllMods:
                {
                    foreach (var mod in mods)
                        if (!mod.Name.StartsWith("DISABLED") || !mod.Name.StartsWith(CharacterModList.DISABLED_PREFIX))
                            mod.Rename(CharacterModList.DISABLED_PREFIX + mod.Name);

                    break;
                }
        }
    }

    private void RemoveJASMSettings(bool removeLocalJasmSettings, IEnumerable<IMod> exportedMods)
    {
        if (removeLocalJasmSettings)
            foreach (var file in exportedMods.Select(mod => new DirectoryInfo(mod.FullPath))
                         .SelectMany(folder =>
                             folder.EnumerateFileSystemInfos($"{Constants.InternalFilePrefix}*", SearchOption.AllDirectories)
                                 .Concat(folder.EnumerateFileSystemInfos($"{Constants.LegacyInternalFilePrefix}*", SearchOption.AllDirectories))))
            {
                _logger.Debug("Deleting local settings file '{SettingsFile}' in modFolder", file.FullName);
                file.Delete();
            }
    }

    public ICharacterModList GetCharacterModList(string internalName)
    {
        var characterModList = _characterModLists.First(x => x.Character.InternalNameEquals(internalName));

        return characterModList;
    }

    public ICharacterModList GetCharacterModList(IModdableObject character) => GetCharacterModList(character.InternalName);


    public ICharacterModList? GetCharacterModListOrDefault(string internalName) =>
        _characterModLists.FirstOrDefault(x => x.Character.InternalNameEquals(internalName));

    public async Task InitializeAsync(string activeModsFolderPath, string? unloadedModsFolderPath = null,
        string? threeMigotoRootfolder = null)
    {
        if (IsInitialized)
        {
            _logger.Error("ModManagerService is already initialized");
            return;
        }

        _logger.Debug(
            "Initializing ModManagerService, activeModsFolderPath: {ActiveModsFolderPath}, unloadedModsFolderPath: {UnloadedModsFolderPath}",
            activeModsFolderPath, unloadedModsFolderPath);
        if (unloadedModsFolderPath is not null)
        {
            _unloadedModsFolder = new DirectoryInfo(unloadedModsFolderPath);
            _unloadedModsFolder.Create();
            UnloadingModsEnabled = true;
        }

        if (threeMigotoRootfolder is not null)
        {
            _threeMigotoFolder = new DirectoryInfo(threeMigotoRootfolder);
            _threeMigotoFolder.Refresh();
            if (!_threeMigotoFolder.Exists)
                throw new InvalidOperationException("3DMigoto folder does not exist");

            //_userIniWatcher = new FileSystemWatcher(_threeMigotoFolder.FullName, D3DX_USER_INI);
            //_userIniWatcher.Changed += OnUserIniChanged;
            //_userIniWatcher.NotifyFilter = NotifyFilters.LastWrite;
            //_userIniWatcher.IncludeSubdirectories = false;
            //_userIniWatcher.EnableRaisingEvents = true;
        }

        _activeModsFolder = new DirectoryInfo(activeModsFolderPath);
        _activeModsFolder.Create();
        InitializeFolderStructure();
        await ScanForModsAsync().ConfigureAwait(false);

        IsInitialized = true;

#if DEBUG
#pragma warning disable CS4014
        Task.Run(DebugDuplicateIdChecker).ConfigureAwait(false);
#pragma warning restore CS4014
#endif
    }

    private void InitializeFolderStructure()
    {
        var categories = _gameService.GetCategories();

        foreach (var category in categories)
        {
            var categoryFolder = GetCategoryFolderPath(category);
            categoryFolder.Create();
        }
    }

    public DirectoryInfo GetCategoryFolderPath(ICategory category)
    {
        return new DirectoryInfo(Path.Combine(_activeModsFolder.FullName, category.InternalName));
    }

    public string GetCharacterModsFolderPath(IModdableObject character, bool disabledPrefix = false)
    {
        var folderPath = GetCharacterModFolderPath(character);
        if (!disabledPrefix)
            return folderPath;

        var dir = new DirectoryInfo(folderPath);
        if (dir.Parent is null)
            return folderPath;

        return Path.Combine(dir.Parent.FullName, ModFolderHelpers.DISABLED_PREFIX + dir.Name);
    }

    public async Task<int> ReorganizeModsAsync(InternalName? characterFolderToReorganize = null,
        bool disableMods = false)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return 0;
    }

    private string GetCharacterModFolderPath(IModdableObject character)
    {
        var category = _gameService.GetCategories()
            .FirstOrDefault(c => c.InternalName.Equals(character.ModCategory.InternalName));

        if (category is null)
            throw new InvalidOperationException(
                $"Failed to get category for character '{character.DisplayName}'");

        return Path.Combine(_activeModsFolder.FullName, category.InternalName, GetCharacterFolderName(character));
    }

    private string GetCharacterFolderName(IModdableObject character)
    {
        var baseName = SanitizeFolderName(character.DisplayName);
        if (baseName.IsNullOrEmpty())
            return character.InternalName;

        var duplicates = _gameService.GetModdableObjects(character.ModCategory, GetOnly.Both)
            .Where(x => !x.InternalNameEquals(character.InternalName))
            .Any(x => SanitizeFolderName(x.DisplayName).Equals(baseName, StringComparison.OrdinalIgnoreCase));

        return duplicates ? $"{baseName} ({character.InternalName})" : baseName;
    }

    private static string SanitizeFolderName(string name)
    {
        if (name.IsNullOrEmpty())
            return string.Empty;

        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        var invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            sb.Append(invalidChars.Contains(c) ? '_' : c);
        }

        var sanitized = sb.ToString().Trim().TrimEnd('.');
        if (sanitized.Length == 0)
            return string.Empty;

        if (IsWindowsReservedDeviceName(sanitized))
            sanitized = sanitized + "_";

        const int maxLen = 80;
        if (sanitized.Length > maxLen)
            sanitized = sanitized[..maxLen].Trim().TrimEnd('.');

        return sanitized;
    }

    private static bool IsWindowsReservedDeviceName(string name)
    {
        var baseName = name.Trim().TrimEnd('.');
        var dotIndex = baseName.IndexOf('.');
        if (dotIndex >= 0)
            baseName = baseName[..dotIndex];

        return baseName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && baseName.Length == 4 &&
               char.IsDigit(baseName[3]) ||
               baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase) && baseName.Length == 4 &&
               char.IsDigit(baseName[3]);
    }

    private sealed record SkinFolderMetadata
    {
        public string SkinInternalName { get; init; } = string.Empty;
        public string? SkinDisplayName { get; init; }
    }

    private static string GetDefaultSkinFolderDisplayName() => "默认皮肤";

    private string GetOrCreateDefaultSkinFolderPath(IModdableObject moddableObject)
    {
        var skinInternalName = moddableObject is ICharacter character
            ? character.Skins.FirstOrDefault(s => s.IsDefault)?.InternalName.Id
            : null;

        skinInternalName ??= $"Default_{moddableObject.InternalName}";

        var displayName = GetDefaultSkinFolderDisplayName();
        return GetOrCreateSkinFolderPath(moddableObject, skinInternalName, displayName);
    }

    private string GetOrCreateSkinFolderPath(IModdableObject moddableObject, string skinInternalName, string preferredDisplayName)
    {
        ArgumentNullException.ThrowIfNull(moddableObject);
        ArgumentNullException.ThrowIfNull(skinInternalName);

        var characterFolderPath = GetCharacterModFolderPath(moddableObject);
        Directory.CreateDirectory(characterFolderPath);

        var characterFolder = new DirectoryInfo(characterFolderPath);
        characterFolder.Refresh();

        foreach (var existingFolder in characterFolder.EnumerateDirectories())
        {
            var metadataPath = Path.Combine(existingFolder.FullName, Constants.SkinFolderMetadataFileName);
            var legacyMetadataPath = Path.Combine(existingFolder.FullName, Constants.LegacySkinFolderMetadataFileName);

            var metadataPathToRead = File.Exists(metadataPath)
                ? metadataPath
                : File.Exists(legacyMetadataPath)
                    ? legacyMetadataPath
                    : null;

            if (metadataPathToRead is null)
                continue;

            SkinFolderMetadata? metadata;
            try
            {
                metadata = JsonSerializer.Deserialize<SkinFolderMetadata>(File.ReadAllText(metadataPathToRead), _skinFolderJsonOptions);
            }
            catch
            {
                continue;
            }

            if (metadata is null)
                continue;

            if (metadata.SkinInternalName.Equals(skinInternalName, StringComparison.OrdinalIgnoreCase))
            {
                if (metadataPathToRead == legacyMetadataPath && !File.Exists(metadataPath))
                    File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, _skinFolderJsonOptions));
                return existingFolder.FullName;
            }
        }

        var baseName = SanitizeFolderName(preferredDisplayName);
        if (baseName.IsNullOrEmpty())
            baseName = SanitizeFolderName(skinInternalName);
        if (baseName.IsNullOrEmpty())
            baseName = "Skin";

        var newFolderName = baseName;
        var counter = 2;
        while (Directory.Exists(Path.Combine(characterFolder.FullName, newFolderName)))
        {
            newFolderName = $"{baseName} ({counter})";
            counter++;
        }

        var skinFolderPath = Path.Combine(characterFolder.FullName, newFolderName);
        Directory.CreateDirectory(skinFolderPath);

        var metadataFilePath = Path.Combine(skinFolderPath, Constants.SkinFolderMetadataFileName);
        var metadataToWrite = new SkinFolderMetadata
        {
            SkinInternalName = skinInternalName,
            SkinDisplayName = preferredDisplayName
        };
        File.WriteAllText(metadataFilePath, JsonSerializer.Serialize(metadataToWrite, _skinFolderJsonOptions));

        return skinFolderPath;
    }

    private DirectoryInfo? TryGetSkinFolderByInternalName(DirectoryInfo characterFolder, string skinInternalName)
    {
        foreach (var existingFolder in characterFolder.EnumerateDirectories())
        {
            var metadataPath = Path.Combine(existingFolder.FullName, Constants.SkinFolderMetadataFileName);
            var legacyMetadataPath = Path.Combine(existingFolder.FullName, Constants.LegacySkinFolderMetadataFileName);

            var metadataPathToRead = File.Exists(metadataPath)
                ? metadataPath
                : File.Exists(legacyMetadataPath)
                    ? legacyMetadataPath
                    : null;

            if (metadataPathToRead is null)
                continue;

            SkinFolderMetadata? metadata;
            try
            {
                metadata = JsonSerializer.Deserialize<SkinFolderMetadata>(File.ReadAllText(metadataPathToRead), _skinFolderJsonOptions);
            }
            catch
            {
                continue;
            }

            if (metadata is null)
                continue;

            if (metadata.SkinInternalName.Equals(skinInternalName, StringComparison.OrdinalIgnoreCase))
            {
                if (metadataPathToRead == legacyMetadataPath && !File.Exists(metadataPath))
                    File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, _skinFolderJsonOptions));
                return existingFolder;
            }
        }

        return null;
    }

    public async Task SyncCharacterFolderNameAsync(IModdableObject moddableObject, string previousDisplayName)
    {
        if (!IsInitialized)
            return;

        var category = _gameService.GetCategories()
            .FirstOrDefault(c => c.InternalName.Equals(moddableObject.ModCategory.InternalName));
        if (category is null)
            return;

        var categoryFolder = GetCategoryFolderPath(category);
        categoryFolder.Refresh();
        if (!categoryFolder.Exists)
            return;

        var newFolderPath = GetCharacterModFolderPath(moddableObject);
        var newFolder = new DirectoryInfo(newFolderPath);
        newFolder.Refresh();

        var previousBaseName = SanitizeFolderName(previousDisplayName);
        var possibleOldFolderNames = new List<string>();
        if (!previousBaseName.IsNullOrEmpty())
        {
            possibleOldFolderNames.Add(previousBaseName);
            possibleOldFolderNames.Add($"{previousBaseName} ({moddableObject.InternalName})");
        }
        possibleOldFolderNames.Add(moddableObject.InternalName);

        DirectoryInfo? oldFolder = null;
        foreach (var oldName in possibleOldFolderNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = new DirectoryInfo(Path.Combine(categoryFolder.FullName, oldName));
            candidate.Refresh();
            if (candidate.Exists)
            {
                oldFolder = candidate;
                break;
            }
        }

        if (oldFolder is not null && !ModFolderHelpers.AbsModFolderCompare(oldFolder.FullName, newFolder.FullName))
        {
            if (!newFolder.Exists)
            {
                Directory.Move(oldFolder.FullName, newFolder.FullName);
                _logger.Information("Renamed character folder '{OldFolder}' -> '{NewFolder}'",
                    oldFolder.Name, newFolder.Name);
            }
            else
            {
                _logger.Warning(
                    "Skipped renaming character folder from '{OldFolder}' to '{NewFolder}' because destination already exists",
                    oldFolder.FullName, newFolder.FullName);
            }
        }

        var existingModList = GetCharacterModListOrDefault(moddableObject.InternalName);
        if (existingModList is null)
            return;

        if (!ModFolderHelpers.AbsModFolderCompare(existingModList.AbsModsFolderPath, newFolder.FullName))
        {
            lock (_modListLock)
            {
                _characterModLists.Remove(existingModList);
                existingModList.Dispose();
            }

            var modList = new CharacterModList(moddableObject, newFolder.FullName, logger: _logger);
            AddNewModList(modList);
        }

        await RefreshModsAsync(moddableObject.InternalName).ConfigureAwait(false);
    }

    public async Task RenameCharacterInternalNameAsync(InternalName oldInternalName, InternalName newInternalName)
    {
        if (!IsInitialized)
            return;

        ArgumentNullException.ThrowIfNull(oldInternalName);
        ArgumentNullException.ThrowIfNull(newInternalName);

        if (oldInternalName.Equals(newInternalName))
            return;

        var newCharacter = _gameService.GetCharacterByIdentifier(newInternalName.Id, includeDisabledCharacters: true);
        if (newCharacter is null)
            return;

        var category = _gameService.GetCategories()
            .FirstOrDefault(c => c.InternalName.Equals(newCharacter.ModCategory.InternalName));
        if (category is null)
            return;

        var categoryFolder = GetCategoryFolderPath(category);
        categoryFolder.Refresh();
        if (!categoryFolder.Exists)
            return;

        var newEnabledPath = GetCharacterModsFolderPath(newCharacter, disabledPrefix: false);
        var newDisabledPath = GetCharacterModsFolderPath(newCharacter, disabledPrefix: true);

        var existingModList = GetCharacterModListOrDefault(oldInternalName.Id);
        if (existingModList is not null)
        {
            var oldEnabledPath = existingModList.AbsModsFolderPath;

            if (Directory.Exists(oldEnabledPath) && !ModFolderHelpers.AbsModFolderCompare(oldEnabledPath, newEnabledPath))
            {
                if (!Directory.Exists(newEnabledPath))
                {
                    Directory.Move(oldEnabledPath, newEnabledPath);
                    _logger.Information("Renamed character folder '{OldFolder}' -> '{NewFolder}'",
                        new DirectoryInfo(oldEnabledPath).Name, new DirectoryInfo(newEnabledPath).Name);
                }
                else
                {
                    _logger.Warning(
                        "Skipped renaming character folder from '{OldFolder}' to '{NewFolder}' because destination already exists",
                        oldEnabledPath, newEnabledPath);
                }
            }

            lock (_modListLock)
            {
                _characterModLists.Remove(existingModList);
                existingModList.Dispose();
            }

            var modList = new CharacterModList(newCharacter, newEnabledPath, logger: _logger);
            AddNewModList(modList);
            await RefreshModsAsync(newInternalName.Id).ConfigureAwait(false);
            return;
        }

        var disabledFolders = categoryFolder
            .EnumerateDirectories(ModFolderHelpers.DISABLED_PREFIX + "*", SearchOption.TopDirectoryOnly)
            .Where(d => d.Name.Contains(oldInternalName.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (disabledFolders.Count != 1)
            return;

        var oldDisabledFolder = disabledFolders[0];
        if (Directory.Exists(oldDisabledFolder.FullName) && !ModFolderHelpers.AbsModFolderCompare(oldDisabledFolder.FullName, newDisabledPath))
        {
            if (!Directory.Exists(newDisabledPath))
            {
                Directory.Move(oldDisabledFolder.FullName, newDisabledPath);
                _logger.Information("Renamed disabled character folder '{OldFolder}' -> '{NewFolder}'",
                    oldDisabledFolder.Name, new DirectoryInfo(newDisabledPath).Name);
            }
            else
            {
                _logger.Warning(
                    "Skipped renaming disabled character folder from '{OldFolder}' to '{NewFolder}' because destination already exists",
                    oldDisabledFolder.FullName, newDisabledPath);
            }
        }
    }

    public async Task SyncCharacterSkinFolderNameAsync(IModdableObject moddableObject, InternalName skinInternalName, string displayName)
    {
        if (!IsInitialized)
            return;

        ArgumentNullException.ThrowIfNull(moddableObject);
        ArgumentNullException.ThrowIfNull(skinInternalName);
        ArgumentNullException.ThrowIfNull(displayName);

        var characterFolderPath = GetCharacterModFolderPath(moddableObject);
        var characterFolder = new DirectoryInfo(characterFolderPath);
        characterFolder.Refresh();
        if (!characterFolder.Exists)
            return;

        var existingSkinFolder = TryGetSkinFolderByInternalName(characterFolder, skinInternalName.Id);
        if (existingSkinFolder is null)
        {
            GetOrCreateSkinFolderPath(moddableObject, skinInternalName.Id, displayName);
            await RefreshModsAsync(moddableObject.InternalName).ConfigureAwait(false);
            return;
        }

        var baseName = SanitizeFolderName(displayName);
        if (baseName.IsNullOrEmpty())
            baseName = SanitizeFolderName(skinInternalName.Id);
        if (baseName.IsNullOrEmpty())
            baseName = "Skin";

        var newFolderName = baseName;
        var counter = 2;
        while (Directory.Exists(Path.Combine(characterFolder.FullName, newFolderName)) &&
               !existingSkinFolder.Name.Equals(newFolderName, StringComparison.OrdinalIgnoreCase))
        {
            newFolderName = $"{baseName} ({counter})";
            counter++;
        }

        if (!existingSkinFolder.Name.Equals(newFolderName, StringComparison.OrdinalIgnoreCase))
        {
            var newFolderPath = Path.Combine(characterFolder.FullName, newFolderName);
            Directory.Move(existingSkinFolder.FullName, newFolderPath);
            existingSkinFolder = new DirectoryInfo(newFolderPath);
        }

        var metadataFilePath = Path.Combine(existingSkinFolder.FullName, Constants.SkinFolderMetadataFileName);
        var metadataToWrite = new SkinFolderMetadata
        {
            SkinInternalName = skinInternalName.Id,
            SkinDisplayName = displayName
        };
        File.WriteAllText(metadataFilePath, JsonSerializer.Serialize(metadataToWrite, _skinFolderJsonOptions));

        await RefreshModsAsync(moddableObject.InternalName).ConfigureAwait(false);
    }

    public string GetOrCreateSkinFolderPath(IModdableObject moddableObject, InternalName? skinInternalName = null, string? skinDisplayName = null)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("SkinManagerService is not initialized");

        if (skinInternalName is null)
            return GetOrCreateDefaultSkinFolderPath(moddableObject);

        if (moddableObject is ICharacter character &&
            character.Skins.FirstOrDefault(s => s.InternalNameEquals(skinInternalName)) is { IsDefault: true })
            return GetOrCreateDefaultSkinFolderPath(moddableObject);

        var preferredDisplayName = skinDisplayName.IsNullOrEmpty() ? skinInternalName.Id : skinDisplayName;
        return GetOrCreateSkinFolderPath(moddableObject, skinInternalName.Id, preferredDisplayName);
    }

    public ICollection<DirectoryInfo> CleanCharacterFolders()
    {
        var deletedFolders = new List<DirectoryInfo>();
        foreach (var characterModList in _characterModLists)
        {
            var characterFolder = new DirectoryInfo(characterModList.AbsModsFolderPath);
            if (!characterFolder.Exists) continue;

            var skinFolders = characterFolder.EnumerateDirectories().ToArray();

            foreach (var skinFolder in skinFolders)
            {
                var modFolders = skinFolder.EnumerateDirectories().ToArray();
                foreach (var modFolder in modFolders)
                {
                    var containsOnlyJasmFiles = modFolder.EnumerateFileSystemInfos().All(ModFolderHelpers.IsJASMFileEntry);
                    if (!containsOnlyJasmFiles) continue;

                    _logger.Information("Deleting mod folder, due to it only containing settings files '{ModFolder}'",
                        modFolder.FullName);

                    modFolder.Delete(true);
                    deletedFolders.Add(modFolder);
                }

                skinFolder.Refresh();
                var skinContainsOnlyJasmFiles = skinFolder.EnumerateFileSystemInfos().All(ModFolderHelpers.IsJASMFileEntry);
                if (!skinContainsOnlyJasmFiles) continue;

                _logger.Information("Deleting skin folder, due to it only containing settings files '{SkinFolder}'",
                    skinFolder.FullName);

                skinFolder.Delete(true);
                deletedFolders.Add(skinFolder);
            }

            if (characterFolder.EnumerateFileSystemInfos().Any()) continue;

            _logger.Information("Deleting empty character folder '{CharacterFolder}'", characterFolder.FullName);
            characterFolder.Delete();
            deletedFolders.Add(characterFolder);
        }

        var categories = _gameService.GetCategories();
        foreach (var directory in _activeModsFolder.EnumerateDirectories())
        {
            if (categories.Any(x => x.InternalName.Equals(directory.Name))) continue;

            if (directory.EnumerateFileSystemInfos().Any()) continue;

            _logger.Information("Deleting unknown empty folder '{Folder}'", directory.FullName);
            directory.Delete();
            deletedFolders.Add(directory);
        }

        return deletedFolders;
    }

    public IList<CharacterSkinEntry> GetAllMods(GetOptions getOptions = GetOptions.All)
    {
        // We get them all to avoid locking for too long
        var allMods = CharacterModLists.SelectMany(x => x.Mods).ToList();

        return getOptions switch
        {
            GetOptions.All => allMods,
            GetOptions.Enabled => allMods.Where(x => x.IsEnabled).ToList(),
            GetOptions.Disabled => allMods.Where(x => !x.IsEnabled).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(getOptions), getOptions, null)
        };
    }

    private void OnUserIniChanged(object sender, FileSystemEventArgs e)
    {
        _logger.Debug("d3dx_user.ini was changed");
        UserIniChanged?.Invoke(this, new UserIniChanged());
    }

    public event EventHandler<UserIniChanged>? UserIniChanged;

    private static string D3DX_USER_INI = Constants.UserIniFileName;

    public async Task<string> GetCurrentSwapVariationAsync(Guid characterSkinEntryId)
    {
        if (_threeMigotoFolder is null || !_threeMigotoFolder.Exists)
            return "3DMigoto folder not set";

        var characterSkinEntry = _characterModLists.SelectMany(x => x.Mods)
            .FirstOrDefault(x => x.Id == characterSkinEntryId);
        if (characterSkinEntry is null)
            throw new InvalidOperationException(
                $"CharacterSkinEntry with id {characterSkinEntryId} was not found in any character mod list");

        var mod = characterSkinEntry.Mod;


        var d3dxUserIni = new FileInfo(Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI));
        if (!d3dxUserIni.Exists)
        {
            _logger.Debug("d3dx_user.ini does not exist in 3DMigoto folder");
            return "Unknown";
        }

        var lines = await File.ReadAllLinesAsync(d3dxUserIni.FullName);

        var sectionStarted = false;
        var returnVar = "Unknown";
        foreach (var line in lines)
        {
            if (IniConfigHelpers.IsComment(line)) continue;
            if (IniConfigHelpers.IsSection(line, "Constants"))
            {
                sectionStarted = true;
                continue;
            }

            if (!sectionStarted) continue;
            if (IniConfigHelpers.IsSection(line)) break;

            var iniKey = IniConfigHelpers.GetIniKey(line);
            if (iniKey is null || !iniKey.EndsWith("swapvar", StringComparison.CurrentCultureIgnoreCase)) continue;

            if (!iniKey.Contains(mod.Name.ToLower())) continue;

            returnVar = IniConfigHelpers.GetIniValue(line) ?? "Unknown";
            break;
        }

        return returnVar;
    }

    public void Dispose()
    {
        _userIniWatcher?.Dispose();
        var allModLists = _characterModLists.ToArray();
        foreach (var modList in allModLists)
        {
            _characterModLists.Remove(modList);
            modList.Dispose();
        }
    }


#if DEBUG
    [DoesNotReturn]
    private async Task DebugDuplicateIdChecker()
    {
        while (true)
        {
            await Task.Delay(1000).ConfigureAwait(false);
            var mods = _characterModLists.SelectMany(x => x.Mods).Select(x => x.Mod);
            var duplicateIds = mods.GroupBy(x => x.Id).Where(x => x.Count() > 1).ToArray();

            if (duplicateIds.Any())
            {
                foreach (var duplicateId in duplicateIds)
                {
                    _logger.Error("Duplicate ID found: {Id}", duplicateId.Key);
                    foreach (var mod in duplicateId)
                        _logger.Error("Mod: {ModName}", mod.Name);
                }

                Debugger.Break();
            }
        }
    }
#endif
}

public class UserIniChanged : EventArgs
{
}

public sealed class ExportProgress : EventArgs
{
    public ExportProgress(double progress, string? modName, string operation)
    {
        Progress = (int)Math.Round(progress);
        ModName = modName;
        Operation = operation;
    }

    public int Progress { get; }
    public string? ModName { get; }
    public string Operation { get; }
}
