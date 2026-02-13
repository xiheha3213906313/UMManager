using UMManager.Core.Entities;
using UMManager.Core.Entities.Mods.Contract;

namespace UMManager.WinUI.ViewModels.CharacterDetailsViewModels;

public record ModChangedMessage(object sender, CharacterSkinEntry SkinEntry, ModSettings? Settings);