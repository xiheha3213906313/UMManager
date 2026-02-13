using FluentValidation;
using UMManager.WinUI.Helpers;
using PathPicker = UMManager.WinUI.ViewModels.SubVms.PathPicker;

namespace UMManager.WinUI.Validators;

public class FolderAccessWarning : AbstractValidator<PathPicker>
{
    public FolderAccessWarning(string message, bool includeWriteTest = true)
    {
        RuleFor(x => x.Path).Must(path =>
            {
                if (string.IsNullOrWhiteSpace(path))
                    return true;

                if (!Directory.Exists(path))
                    return true;

                return includeWriteTest
                    ? FileSystemAccessHelper.CanReadWriteDirectory(path)
                    : FileSystemAccessHelper.CanReadDirectory(path);
            })
            .WithMessage(message)
            .WithSeverity(Severity.Warning);
    }
}

