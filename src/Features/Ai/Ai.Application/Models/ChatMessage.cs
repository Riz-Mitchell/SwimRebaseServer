using System.Text.Json.Serialization;

namespace Ai.Application.Models;

public enum ChatRole
{
    User = 0,
    Assistant = 1,
}

/// <summary>One message in the shape the model API expects: a role plus an ordered list of content blocks.</summary>
public sealed record ChatMessage(ChatRole Role, IReadOnlyList<ChatContentBlock> Content);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextContentBlock), "text")]
[JsonDerivedType(typeof(ToolUseContentBlock), "tool_use")]
[JsonDerivedType(typeof(ToolResultContentBlock), "tool_result")]
public abstract record ChatContentBlock;

public sealed record TextContentBlock(string Text) : ChatContentBlock;

public sealed record ToolUseContentBlock(string Id, string ToolName, string InputJson) : ChatContentBlock;

public sealed record ToolResultContentBlock(string ToolUseId, string ResultJson, bool IsError) : ChatContentBlock;
