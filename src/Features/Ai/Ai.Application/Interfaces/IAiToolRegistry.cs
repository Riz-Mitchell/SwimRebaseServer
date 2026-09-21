namespace Ai.Application.Interfaces;

/// <summary>Resolves the set of tools available to the model. Backed by DI-registered <see cref="IAiToolDefinition"/> instances.</summary>
public interface IAiToolRegistry
{
    IReadOnlyList<IAiToolDefinition> GetAll();

    IAiToolDefinition? Find(string name);
}
