using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Work;
using Microsoft.Extensions.DependencyInjection;

namespace Anichron.Worker.Tests.Unit.Work;

public sealed class WorkHandlerRegistryTests
{
    public sealed record OtherCleanupPayload : IWorkPayload
    {
        public static WorkKind Kind => WorkKind.TokenCleanup;
    }

    public sealed record NumberedRetentionPayload(int Number) : IWorkPayload
    {
        public static WorkKind Kind => WorkKind.WorkItemRetention;
    }

    private static readonly Type[] RealPayloads =
        [typeof(TokenCleanupPayload), typeof(WorkItemRetentionPayload), typeof(CrawlPayload), typeof(BurstDetectionPayload)];

    private static WorkHandlerRegistration[] RealHandlers
        => [
            WorkHandlerRegistration.For<TokenCleanupPayload>(),
            WorkHandlerRegistration.For<WorkItemRetentionPayload>(),
            WorkHandlerRegistration.For<CrawlPayload>(),
            WorkHandlerRegistration.For<BurstDetectionPayload>(),
        ];

    private static Action Building(IEnumerable<WorkHandlerRegistration> registrations, params Type[] payloads)
        => () => _ = new WorkHandlerRegistry(registrations, payloads);

    [Fact]
    public void PayloadTypesIn_Core_FindsEveryConcretePayload()
        => WorkHandlerRegistry.PayloadTypesIn(typeof(IWorkPayload).Assembly)
            .Should().BeEquivalentTo(RealPayloads);

    [Fact]
    public void Constructor_OnePayloadAndOneHandlerPerKind_Builds()
        => Building(RealHandlers, RealPayloads).Should().NotThrow();

    [Fact]
    public void Constructor_FromDependencyInjection_ChecksTheCorePayloads()
    {
        var act = () => new WorkHandlerRegistry([WorkHandlerRegistration.For<TokenCleanupPayload>()]);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Be(string.Join(Environment.NewLine,
                "WorkKind.WorkItemRetention needs exactly one handler, found none.",
                "WorkKind.Crawl needs exactly one handler, found none.",
                "WorkKind.BurstDetection needs exactly one handler, found none."));
    }

    [Fact]
    public void Constructor_KindWithoutPayload_FailsNamingIt()
        => Building(RealHandlers, typeof(TokenCleanupPayload), typeof(CrawlPayload), typeof(BurstDetectionPayload)).Should().Throw<InvalidOperationException>()
            .WithMessage("WorkKind.WorkItemRetention needs exactly one payload type, found none.");

    [Fact]
    public void Constructor_KindWithTwoPayloads_FailsNamingBoth()
        => Building(RealHandlers, [.. RealPayloads, typeof(OtherCleanupPayload)]).Should().Throw<InvalidOperationException>()
            .WithMessage("WorkKind.TokenCleanup needs exactly one payload type, found TokenCleanupPayload, OtherCleanupPayload.");

    [Fact]
    public void Constructor_KindWithTwoHandlers_FailsNamingThem()
        => Building([.. RealHandlers, WorkHandlerRegistration.For<OtherCleanupPayload>()], RealPayloads)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("WorkKind.TokenCleanup needs exactly one handler, found TokenCleanupPayload, OtherCleanupPayload.");

    [Fact]
    public void Constructor_HandlerForAnotherPayloadOfTheKind_FailsNamingBoth()
        => Building([WorkHandlerRegistration.For<OtherCleanupPayload>(), .. RealHandlers[1..]], RealPayloads)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("WorkKind.TokenCleanup's handler takes OtherCleanupPayload, not its payload TokenCleanupPayload.");

    [Fact]
    public void Constructor_SeveralProblems_ReportsEachOnItsOwnLine()
        => Building([], RealPayloads).Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Be(string.Join(Environment.NewLine,
                "WorkKind.TokenCleanup needs exactly one handler, found none.",
                "WorkKind.WorkItemRetention needs exactly one handler, found none.",
                "WorkKind.Crawl needs exactly one handler, found none.",
                "WorkKind.BurstDetection needs exactly one handler, found none."));

    [Fact]
    public async Task RunAsync_DispatchesTheDeserialisedPayloadToTheKindsHandlerAsync()
    {
        var handler = Substitute.For<IWorkHandler<NumberedRetentionPayload>>();
        var progress = Substitute.For<IWorkProgress>();
        using var cts = new CancellationTokenSource();
        var itemId = Guid.NewGuid();
        handler.RunAsync(new NumberedRetentionPayload(7), new WorkRun(itemId, progress), cts.Token).Returns("summary");
        await using var services = new ServiceCollection().AddSingleton(handler).BuildServiceProvider();
        var registry = new WorkHandlerRegistry(
            [RealHandlers[0], WorkHandlerRegistration.For<NumberedRetentionPayload>(), .. RealHandlers[2..]],
            [typeof(TokenCleanupPayload), typeof(NumberedRetentionPayload), typeof(CrawlPayload), typeof(BurstDetectionPayload)]);
        var work = new ClaimedWork(new WorkAttempt(itemId, 1), WorkKind.WorkItemRetention, /*lang=json,strict*/ """{"number":7}""");

        var result = await registry.RunAsync(work, services, progress, cts.Token);

        result.Should().Be("summary");
    }
}
