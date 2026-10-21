using Anichron.API.Endpoints;
using Anichron.API.Settings;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class WorkItemEndpointsTests
{
    private static readonly Instant Start = Instant.FromUtc(2026, 10, 18, 9, 0);
    private static readonly Guid ItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ConfigId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid AdminId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly IWorkItemReader reader = Substitute.For<IWorkItemReader>();
    private readonly IWorkItemCanceller canceller = Substitute.For<IWorkItemCanceller>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Instant At(int seconds) => Start + Duration.FromSeconds(seconds);

    private static WorkItemView View(
        WorkItemStatus status,
        string? result = /*lang=json,strict*/ """{"found": 8}""",
        string? error = "IOException: the root is gone")
        => new(ItemId, WorkKind.Crawl, ConfigId, AdminId, At(1), status, 2, 1, "all@host",
            At(2), At(9), At(9), new WorkProgress(7, 8, "7 found"), result, error, null);

    private void Pages(int total = 1, Instant? latest = null)
    {
        reader.LatestUpdateAsync(Arg.Any<CancellationToken>()).Returns(latest ?? At(30));
        reader.ListAsync(Arg.Any<WorkItemQuery>(), Arg.Any<CancellationToken>())
              .Returns(new WorkItemPage([View(WorkItemStatus.Running)], total));
    }

    // ==========================================================================
    // ListAsync
    // ==========================================================================

    [Fact]
    public async Task ListAsync_NoPaging_ReadsTheFirstPageOfFiftyAsync()
    {
        Pages(total: 137);

        var result = await WorkItemEndpoints.ListAsync(WorkItemStatus.Running, WorkKind.Crawl, null, null, reader, Ct);

        var page = result.Result.Should().BeOfType<Ok<WorkItemPageDto>>().Which.Value!;
        (page.Page, page.PageSize, page.Total).Should().Be((1, 50, 137));
        page.Items.Should().ContainSingle().Which.Id.Should().Be(ItemId);
        await reader.Received(1).ListAsync(
            new WorkItemQuery(WorkItemStatus.Running, WorkKind.Crawl, Skip: 0, Take: 50), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_PageAndSize_SkipThePagesBeforeAsync()
    {
        Pages();

        var result = await WorkItemEndpoints.ListAsync(null, null, 3, 20, reader, Ct);

        var page = result.Result.Should().BeOfType<Ok<WorkItemPageDto>>().Which.Value!;
        (page.Page, page.PageSize).Should().Be((3, 20));
        await reader.Received(1).ListAsync(new WorkItemQuery(null, null, Skip: 40, Take: 20), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_CarriesTheCursorReadBeforeThePageAsync()
    {
        Pages(latest: At(30));

        var result = await WorkItemEndpoints.ListAsync(null, null, null, null, reader, Ct);

        result.Result.Should().BeOfType<Ok<WorkItemPageDto>>().Which.Value!.Cursor.Should().Be("2026-10-18T09:00:30Z");
        Received.InOrder(() =>
        {
            _ = reader.LatestUpdateAsync(Arg.Any<CancellationToken>());
            _ = reader.ListAsync(Arg.Any<WorkItemQuery>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ListAsync_NoItemsYet_CursorIsTheBeginningAsync()
    {
        reader.LatestUpdateAsync(Arg.Any<CancellationToken>()).Returns((Instant?)null);
        reader.ListAsync(Arg.Any<WorkItemQuery>(), Arg.Any<CancellationToken>()).Returns(new WorkItemPage([], 0));

        var result = await WorkItemEndpoints.ListAsync(null, null, null, null, reader, Ct);

        result.Result.Should().BeOfType<Ok<WorkItemPageDto>>().Which.Value!.Cursor.Should().Be("1970-01-01T00:00:00Z");
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(1, 200, 0)]
    [InlineData(10_737_418, 200, 2_147_483_400)]
    [InlineData(2_147_483_647, 1, 2_147_483_646)]
    public async Task ListAsync_PagingInRange_IsReadAsync(int page, int pageSize, int skip)
    {
        Pages();

        var result = await WorkItemEndpoints.ListAsync(null, null, page, pageSize, reader, Ct);

        result.Result.Should().BeOfType<Ok<WorkItemPageDto>>();
        await reader.Received(1).ListAsync(new WorkItemQuery(null, null, skip, pageSize), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 50, "page", "page is out of range.")]
    [InlineData(-1, 50, "page", "page is out of range.")]
    [InlineData(10_737_419, 200, "page", "page is out of range.")]
    [InlineData(1, 0, "pageSize", "pageSize must be from 1 to 200.")]
    [InlineData(1, 201, "pageSize", "pageSize must be from 1 to 200.")]
    public async Task ListAsync_PagingOutOfRange_IsAValidationProblemAsync(int page, int pageSize, string field, string message)
    {
        Pages();

        var result = await WorkItemEndpoints.ListAsync(null, null, page, pageSize, reader, Ct);

        var errors = result.Result.Should().BeOfType<ValidationProblem>().Which.ProblemDetails.Errors;
        errors.Keys.Should().Equal(field);
        errors[field].Should().Equal(message);
        await reader.DidNotReceive().ListAsync(Arg.Any<WorkItemQuery>(), Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // GetAsync
    // ==========================================================================

    [Fact]
    public async Task GetAsync_KnownId_ReturnsTheItemAsync()
    {
        reader.FindAsync(ItemId, Arg.Any<CancellationToken>()).Returns(View(WorkItemStatus.Running));

        var result = await WorkItemEndpoints.GetAsync(ItemId, reader, Ct);

        result.Result.Should().BeOfType<Ok<WorkItemDto>>().Which.Value!.Id.Should().Be(ItemId);
    }

    [Fact]
    public async Task GetAsync_UnknownId_IsNotFoundAsync()
    {
        reader.FindAsync(ItemId, Arg.Any<CancellationToken>()).Returns((WorkItemView?)null);

        var result = await WorkItemEndpoints.GetAsync(ItemId, reader, Ct);

        result.Result.Should().BeOfType<NotFound>();
    }

    // ==========================================================================
    // StreamEvents
    // ==========================================================================

    // ==========================================================================
    // CancelAsync
    // ==========================================================================

    private void Cancels(CancelOutcome outcome, WorkItemStatus statusAfter)
    {
        canceller.CancelAsync(ItemId, Arg.Any<CancellationToken>()).Returns(outcome);
        reader.FindAsync(ItemId, Arg.Any<CancellationToken>()).Returns(View(statusAfter));
    }

    [Fact]
    public async Task CancelAsync_QueuedItem_ReturnsTheCancelledItemAsync()
    {
        Cancels(CancelOutcome.Cancelled, WorkItemStatus.Cancelled);

        var result = await WorkItemEndpoints.CancelAsync(ItemId, canceller, reader, Ct);

        result.Result.Should().BeOfType<Ok<WorkItemDto>>().Which.Value!.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task CancelAsync_RunningItem_IsAcceptedWithTheItemAndItsLocationAsync()
    {
        Cancels(CancelOutcome.Requested, WorkItemStatus.Running);

        var result = await WorkItemEndpoints.CancelAsync(ItemId, canceller, reader, Ct);

        var accepted = result.Result.Should().BeOfType<Accepted<WorkItemDto>>().Which;
        accepted.Location.Should().Be("/api/v1/admin/work-items/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        accepted.Value!.Status.Should().Be("Running");
    }

    [Fact]
    public async Task CancelAsync_FinishedItem_IsAConflictShowingTheItemAsync()
    {
        Cancels(CancelOutcome.Finished, WorkItemStatus.Succeeded);

        var result = await WorkItemEndpoints.CancelAsync(ItemId, canceller, reader, Ct);

        result.Result.Should().BeOfType<Conflict<WorkItemDto>>().Which.Value!.Status.Should().Be("Succeeded");
    }

    [Fact]
    public async Task CancelAsync_UnknownId_IsNotFoundWithoutReadingAsync()
    {
        canceller.CancelAsync(ItemId, Arg.Any<CancellationToken>()).Returns(CancelOutcome.NotFound);

        var result = await WorkItemEndpoints.CancelAsync(ItemId, canceller, reader, Ct);

        result.Result.Should().BeOfType<NotFound>();
        await reader.DidNotReceive().FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CancelOutcome.Cancelled)]
    [InlineData(CancelOutcome.Requested)]
    [InlineData(CancelOutcome.Finished)]
    public async Task CancelAsync_ItemDeletedBeforeItIsRead_IsNotFoundAsync(CancelOutcome outcome)
    {
        canceller.CancelAsync(ItemId, Arg.Any<CancellationToken>()).Returns(outcome);
        reader.FindAsync(ItemId, Arg.Any<CancellationToken>()).Returns((WorkItemView?)null);

        var result = await WorkItemEndpoints.CancelAsync(ItemId, canceller, reader, Ct);

        result.Result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public void StreamEvents_AnswersWithAServerSentEventStream()
    {
        var events = new WorkItemEvents(reader, TimeProvider.System, Options.Create(new WorkItemEventsSettings()));

        WorkItemEndpoints.StreamEvents(null, events, Ct).Should().BeOfType<ServerSentEventsResult<WorkItemDto>>();
    }

    // ==========================================================================
    // WorkItemDtoMapper
    // ==========================================================================

    [Fact]
    public void ToDto_ShowsTheItemWithIsoTimesAndNamedEnums()
    {
        var dto = WorkItemDtoMapper.ToDto(View(WorkItemStatus.Running) with { FinishedAt = null });

        dto.Should().BeEquivalentTo(new
        {
            Id = ItemId,
            Kind = "Crawl",
            StorageConfigId = ConfigId,
            RequestedBy = AdminId,
            Status = "Running",
            Attempt = 2,
            Stalls = 1,
            ClaimedBy = "all@host",
            RequestedAt = "2026-10-18T09:00:01Z",
            StartedAt = "2026-10-18T09:00:02Z",
            FinishedAt = (string?)null,
            UpdatedAt = "2026-10-18T09:00:09Z",
            Progress = new WorkProgressDto(7, 8, "7 found"),
        });
    }

    [Fact]
    public void ToDto_NotStarted_HasNoStartTime()
        => WorkItemDtoMapper.ToDto(View(WorkItemStatus.Queued) with { StartedAt = null }).StartedAt.Should().BeNull();

    [Fact]
    public void ToDto_Finished_HasItsFinishTime()
        => WorkItemDtoMapper.ToDto(View(WorkItemStatus.Succeeded)).FinishedAt.Should().Be("2026-10-18T09:00:09Z");

    [Fact]
    public void ToDto_Succeeded_ShowsTheResultAsJsonAndNoError()
    {
        var dto = WorkItemDtoMapper.ToDto(View(WorkItemStatus.Succeeded));

        dto.Result.Should().NotBeNull();
        dto.Result.Value.GetProperty("found").GetInt32().Should().Be(8);
        dto.Error.Should().BeNull();
    }

    [Fact]
    public void ToDto_SucceededWithoutAResult_ShowsNoResult()
        => WorkItemDtoMapper.ToDto(View(WorkItemStatus.Succeeded, result: null)).Result.Should().BeNull();

    [Fact]
    public void ToDto_Succeeded_ResultIsNotPartial()
        => WorkItemDtoMapper.ToDto(View(WorkItemStatus.Succeeded)).ResultIsPartial.Should().BeFalse();

    [Fact]
    public void ToDto_Failed_ShowsTheErrorAndProgressButNoResult()
    {
        var dto = WorkItemDtoMapper.ToDto(View(WorkItemStatus.Failed));

        dto.Error.Should().Be("IOException: the root is gone");
        dto.Progress.Should().Be(new WorkProgressDto(7, 8, "7 found"));
        dto.Result.Should().BeNull();
        dto.ResultIsPartial.Should().BeFalse();
    }

    [Fact]
    public void ToDto_Cancelled_ShowsTheResultAndProgressMarkedPartial()
    {
        var dto = WorkItemDtoMapper.ToDto(View(WorkItemStatus.Cancelled, error: null));

        dto.Result.Should().NotBeNull();
        dto.Result.Value.GetProperty("found").GetInt32().Should().Be(8);
        dto.Progress.Should().Be(new WorkProgressDto(7, 8, "7 found"));
        dto.ResultIsPartial.Should().BeTrue();
        dto.Error.Should().BeNull();
    }

    [Fact]
    public void ToDto_CancelledAsSuperseded_ShowsTheError()
        => WorkItemDtoMapper.ToDto(View(WorkItemStatus.Cancelled, result: null)).Error
            .Should().Be("IOException: the root is gone");

    [Fact]
    public void ToDto_CancelRequested_ShowsWhen()
        => WorkItemDtoMapper.ToDto(View(WorkItemStatus.Running) with { CancelRequestedAt = At(4) }).CancelRequestedAt
            .Should().Be("2026-10-18T09:00:04Z");

    [Fact]
    public void ToDto_NoCancelRequested_ShowsNone()
        => WorkItemDtoMapper.ToDto(View(WorkItemStatus.Running)).CancelRequestedAt.Should().BeNull();

    [Theory]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Running)]
    public void ToDto_NotFinished_ShowsNeitherResultNorError(WorkItemStatus status)
    {
        var dto = WorkItemDtoMapper.ToDto(View(status));

        dto.Result.Should().BeNull();
        dto.Error.Should().BeNull();
        dto.ResultIsPartial.Should().BeFalse();
    }
}
