using UMManager.Core.GamesService;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.Helpers;
using Serilog;

namespace UMManager.Core.Services;

public class ModCrawlerService
{
    private readonly IGameService _gameService;
    private readonly ILogger _logger;

    public ModCrawlerService(ILogger logger, IGameService gameService)
    {
        _gameService = gameService;
        _logger = logger.ForContext<ModCrawlerService>();
    }


    public IEnumerable<IModdableObject> GetMatchingModdableObjects(
        string absPath, ICollection<IModdableObject>? searchOnlyModdableObjects = null)
    {
        var folder = new DirectoryInfo(absPath);
        if (!folder.Exists) throw new DirectoryNotFoundException($"Could not find folder {folder.FullName}");

        var moddableObjects = searchOnlyModdableObjects ?? _gameService.GetAllModdableObjects();

        foreach (var file in folder.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            var moddableObject = moddableObjects.FirstOrDefault(mo => IsOfModType(file, mo));
            if (moddableObject is null) continue;

            _logger.Verbose("Detected moddableObject {moddableObject} for file {file}", moddableObject.InternalName,
                file.FullName);

            yield return moddableObject;
        }
    }

    public IEnumerable<ICharacterSkin> GetSubSkinsRecursive(string absPath)
    {
        var folder = new DirectoryInfo(absPath);
        if (!folder.Exists) throw new DirectoryNotFoundException($"Could not find folder {folder.FullName}");
        yield break;
    }

    public ICharacterSkin? GetFirstSubSkinRecursive(string absPath, string? internalName = null,
        CancellationToken cancellationToken = default)
    {
        var folder = new DirectoryInfo(absPath);
        if (!folder.Exists) throw new DirectoryNotFoundException($"Could not find folder {folder.FullName}");
        return null;
    }


    private bool IsOfModType(FileInfo file, IModdableObject moddableObject) => false;


    private static IEnumerable<FileInfo> RecursiveGetFiles(DirectoryInfo directoryInfo)
    {
        var files = directoryInfo.GetFiles();

        foreach (var fileInfo in files)
            yield return fileInfo;

        foreach (var directory in directoryInfo.GetDirectories())
            foreach (var directoryFiles in RecursiveGetFiles(directory))
                yield return directoryFiles;
    }


    public FileInfo? GetFirstJasmConfigFileAsync(DirectoryInfo directoryInfo, bool recursive = true)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        return directoryInfo.GetFiles(Constants.ModConfigFileName, searchOption).FirstOrDefault() ??
               directoryInfo.GetFiles(Constants.LegacyModConfigFileName, searchOption).FirstOrDefault();
    }

    public FileInfo? GetMergedIniFile(DirectoryInfo directoryInfo)
    {
        foreach (var file in directoryInfo.EnumerateFiles("*.ini", SearchOption.AllDirectories))
        {
            if (Constants.ScriptIniNames.Any(iniNames =>
                    iniNames.Equals(file.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                return file;
        }

        return null;
    }

    public DirectoryInfo? GetShaderFixesFolder(DirectoryInfo directoryInfo)
    {
        foreach (var dir in directoryInfo.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            if (dir.Name.Trim().Equals(Constants.ShaderFixesFolderName, StringComparison.OrdinalIgnoreCase))
                return dir;
        }

        return null;
    }
}
