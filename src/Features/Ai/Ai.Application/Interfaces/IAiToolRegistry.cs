namespace Ai.Application.Interfaces;

/// <summary>Resolves the set of tools available to the model. Backed by DI-registered <see cref="IAiTool"/> instances.</summary>
public interface IAiToolRegistry
{
    IReadOnlyList<IAiTool> GetAll();

    IAiTool? Find(string name);
}
