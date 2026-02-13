using System.Linq;
using System.Reflection;
using UMManager.Core.Helpers;

namespace UMManager.Core.GamesService.Requests;

public class UpdateUiCategoryRequest
{
    public NewValue<string> Name { get; set; }
    public NewValue<Uri?> Image { get; set; }
    public NewValue<bool> IsHidden { get; set; }

    public bool AnyValuesSet => GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.PropertyType.IsAssignableTo(typeof(ISettableProperty)))
        .Any(p => (p.GetValue(this) as ISettableProperty)?.IsSet == true);
}
