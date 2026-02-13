namespace UMManager.Core.Helpers;

public static class Constants
{
    public static readonly IReadOnlyCollection<string> SupportedImageExtensions = new[]
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".svg", ".webp", ".bitmap" };

    public static readonly IReadOnlyList<string> SupportedArchiveTypes = [".zip", ".rar", ".7z"];

    public static readonly string ModConfigFileName = ".UMManager_ModConfig.json";
    public static readonly string LegacyModConfigFileName = ".JASM_ModConfig.json";

    public static readonly string SkinFolderMetadataFileName = ".UMManager_SkinFolder.json";
    public static readonly string LegacySkinFolderMetadataFileName = ".JASM_SkinFolder.json";

    public const string InternalFilePrefix = ".UMManager_";
    public const string LegacyInternalFilePrefix = ".JASM_";
    public static readonly string ShaderFixesFolderName = "ShaderFixes";
    public static readonly string[] ScriptIniNames = ["Script.ini", "merged.ini", "mod.ini"];
    public static readonly string UserIniFileName = "d3dx_user.ini";
}
