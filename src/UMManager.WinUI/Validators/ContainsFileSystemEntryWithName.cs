using FluentValidation;
using UMManager.WinUI.ViewModels.SubVms;

namespace UMManager.WinUI.Validators;

public class ContainsFileSystemEntryWithName : AbstractValidator<PathPicker>
{
    public ContainsFileSystemEntryWithName(string filename, string? customMessage = null, bool warning = false)
    {
        filename = filename.ToLower();
        customMessage ??= $"Folder does not contain {filename}";
        RuleFor(x => x.Path)
            .Must(path =>
                path is not null &&
                Directory.Exists(path) &&
                Directory.GetFileSystemEntries(path).Any(entry => entry.ToLower().EndsWith(filename))
            )
            .WithMessage(customMessage)
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}