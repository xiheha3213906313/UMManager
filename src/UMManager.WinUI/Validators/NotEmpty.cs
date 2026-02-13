using FluentValidation;
using PathPicker = UMManager.WinUI.ViewModels.SubVms.PathPicker;

namespace UMManager.WinUI.Validators;

public class NotEmpty : AbstractValidator<PathPicker>
{
    public NotEmpty(bool warning = false)
    {
        RuleFor(x => x.Path).NotEmpty().WithMessage("Folder path cannot be empty")
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}