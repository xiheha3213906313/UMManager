using UMManager.Core.GamesService.Interfaces;

namespace UMManager.Core.GamesService.Models;

public class GameObject : BaseModdableObject, IGameObject
{
    protected internal GameObject(IModdableObject moddableObject) : base(moddableObject)
    {
    }
}

public interface IGameObject : IModdableObject
{
}