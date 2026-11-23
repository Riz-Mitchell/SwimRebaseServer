using Microsoft.EntityFrameworkCore;
using Sessions.Domain.Models;
using Sessions.Infrastructure;

namespace SwimRebase.Tests.Sessions;

/// <summary>
/// Pins what the database does when a session is hard-deleted (DELETE /api/sessions/{id}): its facts go with it, but timed swims
/// logged against it are their own records and only lose the link.
/// </summary>
public class SessionDeleteBehaviourTests
{
    private static Microsoft.EntityFrameworkCore.Metadata.IForeignKey ForeignKeyToSession<TDependent>()
    {
        var options = new DbContextOptionsBuilder<SessionsDbContext>().UseNpgsql("Host=localhost").UseSnakeCaseNamingConvention().Options;
        using var context = new SessionsDbContext(options);
        return Assert.Single(context.Model.FindEntityType(typeof(TDependent))!.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Session));
    }

    [Fact]
    public void Deleting_a_session_clears_the_link_on_its_timed_swims_instead_of_deleting_them()
    {
        var fk = ForeignKeyToSession<TimedSwim>();

        Assert.Equal(DeleteBehavior.SetNull, fk.DeleteBehavior);
        Assert.False(fk.IsRequired);
    }

    [Fact]
    public void Deleting_a_session_deletes_its_facts()
    {
        Assert.Equal(DeleteBehavior.Cascade, ForeignKeyToSession<SessionFact>().DeleteBehavior);
    }

    [Fact]
    public void The_migrations_create_the_timed_swim_link_with_on_delete_set_null()
    {
        var migrations = Path.Combine(RepoRoot(), "src", "Features", "Sessions", "Sessions.Infrastructure", "Migrations");
        var source = File.ReadAllText(Directory.GetFiles(migrations, "*_AddTimedSwims.cs").Single());
        var fk = source[source.IndexOf("fk_timed_swims_sessions_session_id", StringComparison.Ordinal)..];

        Assert.Contains("onDelete: ReferentialAction.SetNull", fk[..fk.IndexOf(')')]);
        // No later migration may have re-created the constraint with a different action.
        Assert.DoesNotContain(
            Directory.GetFiles(migrations, "*.cs").Where(f => !f.EndsWith("_AddTimedSwims.cs") && !f.EndsWith(".Designer.cs") && !f.EndsWith("ModelSnapshot.cs")),
            f => File.ReadAllText(f).Contains("fk_timed_swims_sessions_session_id"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SwimRebase.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
