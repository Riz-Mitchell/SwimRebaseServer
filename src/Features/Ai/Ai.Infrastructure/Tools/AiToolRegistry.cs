using Ai.Application.Interfaces;

namespace Ai.Infrastructure.Tools;

/// <summary>Aggregates every DI-registered <see cref="IAiTool"/> into a name-keyed lookup.</summary>
public sealed class AiToolRegistry(IEnumerable<IAiTool> tools) : IAiToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAiTool> _byName = tools.ToDictionary(t => t.Name);

    public IReadOnlyList<IAiTool> GetAll() => _byName.Values.ToList();

    public IAiTool? Find(string name) => _byName.GetValueOrDefault(name);
}
