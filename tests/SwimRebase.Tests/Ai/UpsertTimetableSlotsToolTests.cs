using Ai.Infrastructure.Tools;

namespace SwimRebase.Tests.Ai;

public class UpsertTimetableSlotsToolTests
{
    private readonly UpsertTimetableSlotsTool _tool = new(null!);

    [Fact]
    public async Task The_preview_lists_every_slot_from_monday_with_the_effective_date()
    {
        const string input = """
            {"effectiveFrom":"2026-10-12","slots":[
              {"dayOfWeek":"Sunday","startTime":"08:00","endTime":"09:00","sessionType":"Gym"},
              {"dayOfWeek":"Wednesday","startTime":"06:00","endTime":"07:30","sessionType":"Swim","label":"Threshold"},
              {"dayOfWeek":"Monday","startTime":"06:00","endTime":"07:30","sessionType":"Swim"}]}
            """;

        var preview = await _tool.PreviewAsync(Guid.NewGuid(), input, CancellationToken.None);

        Assert.Equal(
            "From 2026-10-12:\nMonday 06:00-07:30 Swim\nWednesday 06:00-07:30 Swim - Threshold\nSunday 08:00-09:00 Gym",
            preview);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"effectiveFrom":"2026-10-12","slots":[]}""")]
    [InlineData("""{"effectiveFrom":"2026-10-12"}""")]
    public async Task The_preview_is_null_when_there_is_nothing_to_show(string input)
    {
        Assert.Null(await _tool.PreviewAsync(Guid.NewGuid(), input, CancellationToken.None));
    }
}
