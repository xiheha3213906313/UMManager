using FluentValidation;
using PathPicker = UMManager.WinUI.ViewModels.SubVms.PathPicker;

namespace UMManager.WinUI.Validators;

public class IsValidPathFormat : AbstractValidator<PathPicker>
{
    public IsValidPathFormat(bool warning = false)
    {
        RuleFor(x => x.Path)
            .Must(path => path is not null && Path.IsPathFullyQualified(path))
            .WithMessage("Path is not valid")
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}