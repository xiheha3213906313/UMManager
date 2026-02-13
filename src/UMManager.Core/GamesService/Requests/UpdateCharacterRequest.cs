﻿﻿﻿﻿﻿﻿﻿using System.Reflection;
using UMManager.Core.Helpers;

namespace UMManager.Core.GamesService.Requests;

public class UpdateCharacterRequest
{
    public NewValue<string> DisplayName { get; set; }

    public NewValue<bool> IsMultiMod { get; set; }

    public NewValue<Uri?> Image { get; set; }

    public NewValue<string[]> Keys { get; set; }

    public NewValue<int> Rarity { get; set; }

    public NewValue<string> Element { get; set; }

    public NewValue<string> Class { get; set; }

    public NewValue<string[]> Region { get; set; }

    public bool AnyValuesSet => GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.PropertyType.IsAssignableTo(typeof(ISettableProperty)))
        .Any(p => (p.GetValue(this) as ISettableProperty)?.IsSet == true);
}

