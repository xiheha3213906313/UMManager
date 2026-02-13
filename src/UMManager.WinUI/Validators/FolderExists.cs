using FluentValidation;
using PathPicker = UMManager.WinUI.ViewModels.SubVms.PathPicker;

namespace UMManager.WinUI.Validators;

public class FolderExists : AbstractValidator<PathPicker>
{
    public FolderExists(string message = "Folder does not exist", bool warning = false)
    {
        RuleFor(x => x.Path).Must(Directory.Exists)
            .WithMessage(message)
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}