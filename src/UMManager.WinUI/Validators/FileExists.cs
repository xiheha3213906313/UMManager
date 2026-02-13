using FluentValidation;
using PathPicker = UMManager.WinUI.ViewModels.SubVms.PathPicker;

namespace UMManager.WinUI.Validators;

public class FileExists : AbstractValidator<PathPicker>
{
    public FileExists(string message = "File does not exist", bool warning = false)
    {
        RuleFor(x => x.Path).Must(File.Exists)
            .WithMessage(message)
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}