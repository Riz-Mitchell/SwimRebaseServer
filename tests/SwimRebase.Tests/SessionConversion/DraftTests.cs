using System.Text.Json;
using SessionConversion.Application.Interview;
using SessionConversion.Application.Services;
using SessionConversion.Domain.Models;

namespace SwimRebase.Tests.SessionConversion;

public class DraftTests
{
    /// <summary>Roughly what the interviewer emits: properties in any order, enums as lowercase strings.</summary>
    internal const string ModelSession = """
        {"group":"Lane 2","sets":[
          {"reps":4,"dist":100,"strokes":["free"],"kind":"item","rest":20},
          {"kind":"group","repeat":3,"items":[
            {"kind":"item","reps":1,"dist":200,"strokes":["free"],"mode":"kick"},
            {"kind":"item","reps":2,"dist":50,"strokes":["fly"],"equip":["fins"]}]},
          {"kind":"item","reps":1,"dist":400,"strokes":["im"]}]}
        """;

    private static SwimSession Sample() => SessionJson.Deserialize<SwimSession>(ModelSession);

    [Fact]
    public void A_model_shaped_session_deserializes_with_out_of_order_kind()
    {
        var session = Sample();

        Assert.Equal("Lane 2", session.Group);
        Assert.Equal(3, session.Sets.Count);
        Assert.IsType<SetItem>(session.Sets[0]);
        var group = Assert.IsType<SetGroup>(session.Sets[1]);
        Assert.Equal(2, group.Items.Count);
        Assert.Equal(SwimMode.Kick, ((SetItem)group.Items[0]).Mode);
        Assert.Equal(new[] { Stroke.Im }, ((SetItem)session.Sets[2]).Strokes);
    }

    [Fact]
    public void Serializing_and_reading_back_gives_the_same_session()
    {
        var again = SessionJson.Deserialize<SwimSession>(SessionJson.Serialize(Sample()));

        Assert.Equal(SessionJson.Serialize(Sample()), SessionJson.Serialize(again));
    }

    [Fact]
    public void Outline_numbers_sets_and_nested_items_and_shows_totals()
    {
        var outline = SessionText.Outline(Sample());

        Assert.Contains("Group: Lane 2", outline);
        Assert.Contains("Distance: 1700", outline);
        Assert.Contains("[0] 4 x 100 free, rest 20s", outline);
        Assert.Contains("[1] 3 rounds", outline);
        Assert.Contains("[1.0] 1 x 200 free kick", outline);
        Assert.Contains("[1.1] 2 x 50 fly with fins", outline);
        Assert.Contains("[2] 1 x 400 im", outline);
    }

    [Fact]
    public void Remove_replace_and_insert_address_top_level_and_nested_sets()
    {
        var edited = DraftEditor.Apply(Sample(),
        [
            new DraftEdit(DraftEditOps.Remove, "1.0", null, null),
            new DraftEdit(DraftEditOps.Replace, "0", new SetItem { Reps = 5, Dist = 100, Strokes = [Stroke.Free] }, null),
            new DraftEdit(DraftEditOps.Insert, "3", new SetItem { Reps = 1, Dist = 200, Strokes = [Stroke.Free] }, null),
        ]);

        Assert.Equal(4, edited.Sets.Count);
        Assert.Equal(5, ((SetItem)edited.Sets[0]).Reps);
        Assert.Single(((SetGroup)edited.Sets[1]).Items);
        Assert.Equal(200, ((SetItem)edited.Sets[3]).Dist);
    }

    [Fact]
    public void Edits_do_not_change_the_session_they_were_applied_to()
    {
        var original = Sample();
        var before = SessionJson.Serialize(original);

        DraftEditor.Apply(original, [new DraftEdit(DraftEditOps.Remove, "0", null, null)]);

        Assert.Equal(before, SessionJson.Serialize(original));
    }

    [Fact]
    public void Set_group_changes_or_clears_the_group()
    {
        Assert.Equal("Lane 1", DraftEditor.Apply(Sample(), [new DraftEdit(DraftEditOps.SetGroup, null, null, " Lane 1 ")]).Group);
        Assert.Null(DraftEditor.Apply(Sample(), [new DraftEdit(DraftEditOps.SetGroup, null, null, "")]).Group);
    }

    [Theory]
    [InlineData("remove", "9")]
    [InlineData("remove", "0.0")]
    [InlineData("remove", "abc")]
    [InlineData("insert", "7")]
    [InlineData("replace", "0")]
    [InlineData("explode", "0")]
    public void Bad_edits_are_rejected_with_a_message_for_the_model(string op, string path)
    {
        var ex = Assert.Throws<InvalidDraftEditException>(() => DraftEditor.Apply(Sample(), [new DraftEdit(op, path, null, null)]));

        Assert.StartsWith("edit 0 (", ex.Message);
    }

    [Fact]
    public void An_edit_request_is_read_from_the_tool_input()
    {
        var id = Guid.NewGuid();
        var json = $$$"""
            {"draftId":"{{{id}}}","edits":[
              {"op":"remove","path":"1.0"},
              {"op":"replace","path":"0","node":{"kind":"item","reps":6,"dist":50}},
              {"op":"set_group","group":"Lane 3"}]}
            """;

        var request = DraftEditRequest.Parse(json);

        Assert.Equal(id, request.DraftId);
        Assert.Equal(3, request.Edits.Count);
        Assert.Equal(6, ((SetItem)request.Edits[1].Node!).Reps);
        Assert.Equal("Lane 3", request.Edits[2].Group);
    }

    [Fact]
    public void An_edit_request_without_a_valid_draft_id_is_rejected()
    {
        Assert.Throws<InvalidDraftEditException>(() => DraftEditRequest.Parse("""{"draftId":"nope","edits":[]}"""));
    }

    [Fact]
    public async Task Creating_a_draft_stores_it_and_editing_bumps_the_version()
    {
        var service = new SessionDraftService(new InMemoryDraftRepository(), new NoOpUnitOfWork());
        var userId = Guid.NewGuid();

        var created = await service.CreateAsync(userId, Guid.NewGuid(), ModelSession, CancellationToken.None);
        var edited = await service.EditAsync(userId, created.Id, [new DraftEdit(DraftEditOps.Remove, "0", null, null)], CancellationToken.None);

        Assert.Equal(1, created.Version);
        Assert.Equal(2, edited.Version);
        Assert.Equal(2, edited.Session.Sets.Count);
    }

    [Fact]
    public async Task An_invalid_session_is_never_stored()
    {
        var repository = new InMemoryDraftRepository();
        var service = new SessionDraftService(repository, new NoOpUnitOfWork());

        var ex = await Assert.ThrowsAsync<InvalidSessionException>(() =>
            service.CreateAsync(Guid.NewGuid(), null, """{"sets":[{"kind":"item","reps":0,"dist":100}]}""", CancellationToken.None));

        Assert.Contains("reps must be >= 1", ex.Message);
        Assert.Empty(repository.Drafts);
    }

    [Fact]
    public async Task A_draft_belongs_to_its_user()
    {
        var service = new SessionDraftService(new InMemoryDraftRepository(), new NoOpUnitOfWork());
        var created = await service.CreateAsync(Guid.NewGuid(), null, ModelSession, CancellationToken.None);

        await Assert.ThrowsAsync<DraftNotFoundException>(() =>
            service.EditAsync(Guid.NewGuid(), created.Id, [new DraftEdit(DraftEditOps.Remove, "0", null, null)], CancellationToken.None));
    }

    [Fact]
    public void The_model_facing_schemas_are_valid_json_with_shared_definitions()
    {
        foreach (var schema in new[] { InterviewTools.AskSchema, InterviewTools.DraftSessionSchema, InterviewTools.EditDraftSchema, InterviewTools.ReadSwimBoardSchema })
        {
            using var document = JsonDocument.Parse(schema);
            Assert.Equal("object", document.RootElement.GetProperty("type").GetString());
        }

        using var draft = JsonDocument.Parse(InterviewTools.DraftSessionSchema);
        Assert.True(draft.RootElement.GetProperty("$defs").TryGetProperty("node", out _));
        using var edit = JsonDocument.Parse(InterviewTools.EditDraftSchema);
        Assert.True(edit.RootElement.GetProperty("$defs").TryGetProperty("part", out _));
    }
}
