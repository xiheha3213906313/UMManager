using FluentValidation;
using UMManager.WinUI.ViewModels.SubVms;

namespace UMManager.WinUI.Validators.PreConfigured;

public static class UnloadedModsValidators
{
    public static IEnumerable<AbstractValidator<PathPicker>> Validators => new AbstractValidator<PathPicker>[]
    {
        new IsValidPathFormat(),
        new FolderExists("Folder does not exist and will be created", true)
    };
}