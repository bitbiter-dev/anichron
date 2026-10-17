using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Anichron.Core.Tests.Unit.Data;

public sealed class SchemaScriptTests
{
    private static string FindCommittedSchema()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "schema.sql");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            $"docs/schema.sql was not found above {AppContext.BaseDirectory}.");
    }

    private static string Normalise(string sql) => sql.ReplaceLineEndings("\n").Trim();

    [Fact]
    public void GenerateCreateScript_MatchesTheCommittedSchema()
    {
        using var context = AnichronDbContexts.NpgsqlModel();

        var generated = context.Database.GenerateCreateScript();

        Normalise(generated).Should().Be(Normalise(File.ReadAllText(FindCommittedSchema())),
            "docs/schema.sql is the authority on the schema; run scripts/derive-db-docs.sh if the model changed on purpose");
    }

    [Fact]
    public void Invites_XminIsAnOptimisticConcurrencyTokenGeneratedByTheDatabase()
    {
        using var context = AnichronDbContexts.NpgsqlModel();

        var xmin = context.Model.FindEntityType(typeof(Invite))!.FindProperty("xmin");

        xmin.Should().NotBeNull();
        xmin.IsConcurrencyToken.Should().BeTrue();
        xmin.ValueGenerated.Should().Be(ValueGenerated.OnAddOrUpdate);
        xmin.GetColumnName().Should().Be("xmin");
    }
}
