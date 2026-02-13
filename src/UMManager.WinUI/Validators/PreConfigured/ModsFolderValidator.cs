using FluentValidation;
using UMManager.WinUI.Validators;
using PathPicker = UMManager.WinUI.ViewModels.SubVms.PathPicker;

namespace UMManager.WinUI.Validators.PreConfigured;

public static class ModsFolderValidator
{
    public static IEnumerable<AbstractValidator<PathPicker>> Validators => new AbstractValidator<PathPicker>[]
    {
        new IsValidPathFormat(),
        new FolderExists("文件夹不存在"),
        new FolderAccessWarning("该文件夹可能需要管理员权限才能访问。请修改文件夹权限或以管理员身份启动。")
    };
}
