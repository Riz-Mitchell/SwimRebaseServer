using Ai.Application.Interfaces;
using Ai.Application.Models;

namespace SwimRebase.Tests;

/// <summary>Answers every title request with a fixed result and records what it was asked. <see cref="OnGenerate"/> runs first, so a test can act mid-call.</summary>
internal sealed class FakeTitler(TitleResult result) : IConversationTitler
{
    public List<(string Message, bool HasImages)> Calls { get; } = [];

    public Action? OnGenerate { get; init; }

    /// <summary>Thrown from the call itself, before any task exists.</summary>
    public Exception? Throws { get; init; }

    /// <summary>Returned as a faulted task.</summary>
    public Exception? Faults { get; init; }

    public Task<TitleResult> GenerateAsync(string firstMessage, bool hasImages, CancellationToken cancellationToken)
    {
        Calls.Add((firstMessage, hasImages));
        OnGenerate?.Invoke();
        if (Throws is not null)
        {
            throw Throws;
        }

        return Faults is null ? Task.FromResult(result) : Task.FromException<TitleResult>(Faults);
    }
}

/// <summary>A model that plays back fixed events, or throws, or never answers - for exercising the titler's failure handling.</summary>
internal sealed class StubChatClient(Func<CancellationToken, IAsyncEnumerable<ChatStreamEvent>> respond) : IAiChatClient
{
    public List<ChatTurnRequest> Requests { get; } = [];

    public IAsyncEnumerable<ChatStreamEvent> StreamTurnAsync(ChatTurnRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return respond(cancellationToken);
    }

    public static StubChatClient Replying(string text, string model = "haiku") => new(_ => Events(
        new TextDeltaEvent(text),
        new TurnCompletedEvent("end_turn", [], text, "[]", new ChatTokenUsage(model, 12, 4, 0, 0))));

    public static async IAsyncEnumerable<ChatStreamEvent> Events(params ChatStreamEvent[] events)
    {
        foreach (var chatEvent in events)
        {
            yield return chatEvent;
        }

        await Task.CompletedTask;
    }
}
