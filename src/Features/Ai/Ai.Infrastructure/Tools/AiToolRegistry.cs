using Ai.Application.Interfaces;

namespace Ai.Infrastructure.Tools;

/// <summary>Aggregates every DI-registered <see cref="IAiToolDefinition"/> into a name-keyed lookup.</summary>
public sealed class AiToolRegistry(IEnumerable<IAiToolDefinition> tools) : IAiToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAiToolDefinition> _byName = tools.ToDictionary(t => t.Name);

    public IReadOnlyList<IAiToolDefinition> GetAll() => _byName.Values.ToList();

    public IAiToolDefinition? Find(string name) => _byName.GetValueOrDefault(name);
}
