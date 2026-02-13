using System;
using System.Collections.Generic;

namespace UMManager.WinUI.ViewModels.Messages;

public record CharacterTagsChangedMessage(object sender, string CharacterInternalName, IReadOnlyCollection<Guid> TagIds);
