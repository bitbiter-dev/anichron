using Anichron.Core.Domain;
using Anichron.Core.Work;
using NodaTime;
using System.Text.Json;

namespace Anichron.Core.Tests.Unit.Work;

public sealed class WorkJsonTests
{
    private sealed record Timed(Instant At, string Name) : IWorkPayload
    {
        public static WorkKind Kind => WorkKind.TokenCleanup;
    }

    private sealed record Summary(int Deleted, Instant Before);

    private static readonly Instant Moment = Instant.FromUtc(2026, 10, 18, 9, 30);

    [Fact]
    public void Serialize_WritesCamelCaseAndIsoInstants()
        => WorkJson.Serialize(new Timed(Moment, "sweep"))
            .Should().Be(/*lang=json,strict*/ """{"at":"2026-10-18T09:30:00Z","name":"sweep"}""");

    [Fact]
    public void Deserialize_ReadsWhatSerializeWrote()
        => WorkJson.Deserialize<Timed>(WorkJson.Serialize(new Timed(Moment, "sweep")))
            .Should().Be(new Timed(Moment, "sweep"));

    [Fact]
    public void Deserialize_NullPayload_ThrowsNamingTheKind()
    {
        var act = () => WorkJson.Deserialize<Timed>("null");

        act.Should().Throw<JsonException>().WithMessage("The payload of a TokenCleanup item is null.");
    }

    [Fact]
    public void SerializeResult_WritesTheRuntimeTypesProperties()
        => WorkJson.SerializeResult(new Summary(3, Moment))
            .Should().Be(/*lang=json,strict*/ """{"deleted":3,"before":"2026-10-18T09:30:00Z"}""");

    [Fact]
    public void SerializeResult_NoResult_IsNull()
        => WorkJson.SerializeResult(null).Should().BeNull();
}
