using Anichron.API.Endpoints;
using Anichron.API.Settings;
using Anichron.API.Tests.Unit.Infrastructure;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class WorkItemEndpointsHttpTests
{
    private static readonly Instant Start = Instant.FromUtc(2026, 10, 18, 9, 0);
    private static readonly Guid AdminId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid ItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly IWorkItemReader reader = Substitute.For<IWorkItemReader>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WorkItemView Succeeded
        => new(ItemId, WorkKind.Crawl, null, null, Start, WorkItemStatus.Succeeded, 1, 0, "all@host",
            Start, Start + Duration.FromSeconds(9), Start + Duration.FromSeconds(9), 8, 8, "8 found",
            /*lang=json,strict*/ """{"found": 8}""", null);

    private Task<AuthenticatedTestApp> StartAsync()
        => AuthenticatedTestApp.StartAsync(api => api.MapWorkItemEndpoints(), services =>
        {
            services.AddSingleton(reader);
            services.AddSingleton<TimeProvider>(new ImmediateTimers());
            services.AddSingleton(Options.Create(new WorkItemEventsSettings()));
            services.AddScoped<WorkItemEvents>();
        });

    private sealed class ImmediateTimers : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => base.CreateTimer(callback, state, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public async Task List_BindsTheFiltersAndPagingFromTheQueryAsync()
    {
        reader.ListAsync(Arg.Any<WorkItemQuery>(), Arg.Any<CancellationToken>()).Returns(new WorkItemPage([Succeeded], 11));
        await using var app = await StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(
            AuthenticatedTestApp.GetAsAdmin("/api/v1/admin/work-items?status=Succeeded&kind=Crawl&page=2&pageSize=10", AdminId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await reader.Received(1).ListAsync(
            new WorkItemQuery(WorkItemStatus.Succeeded, WorkKind.Crawl, Skip: 10, Take: 10), Arg.Any<CancellationToken>());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("total").GetInt32().Should().Be(11);
        var item = body.RootElement.GetProperty("items")[0];
        item.GetProperty("status").GetString().Should().Be("Succeeded");
        item.GetProperty("result").GetProperty("found").GetInt32().Should().Be(8);
    }

    [Fact]
    public async Task List_UnknownStatus_IsABadRequestAsync()
    {
        await using var app = await StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(
            AuthenticatedTestApp.GetAsAdmin("/api/v1/admin/work-items?status=Sleeping", AdminId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/v1/admin/work-items")]
    [InlineData("/api/v1/admin/work-items/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("/api/v1/admin/work-items/events")]
    public async Task EveryRoute_ForANonAdmin_IsForbiddenAsync(string path)
    {
        await using var app = await StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(AuthenticatedTestApp.Get(path, AdminId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Events_ResumeFromLastEventIdAndFrameEachItemAsync()
    {
        var polls = 0;
        reader.UpdatedSinceAsync(Arg.Any<Instant>(), Arg.Any<CancellationToken>()).Returns(_ => ++polls == 1
            ? Task.FromResult<IReadOnlyList<WorkItemView>>([Succeeded])
            : throw new InvalidOperationException("The stream is read once."));
        await using var app = await StartAsync();
        using var client = app.Client;
        using var request = AuthenticatedTestApp.GetAsAdmin("/api/v1/admin/work-items/events", AdminId);
        request.Headers.Add("Last-Event-ID", "2026-10-18T09:00:05Z");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        await reader.Received(1).UpdatedSinceAsync(Start - Duration.FromSeconds(5), Arg.Any<CancellationToken>());
        var frame = await FirstFrameAsync(response);
        frame.Should().HaveCount(3).And.Contain("event: work-item").And.Contain("id: 2026-10-18T09:00:09Z");
        var json = frame.Should().ContainSingle(line => line.StartsWith("data: ", StringComparison.Ordinal)).Which;
        using var data = JsonDocument.Parse(json["data: ".Length..]);
        data.RootElement.GetProperty("id").GetGuid().Should().Be(ItemId);
        data.RootElement.GetProperty("progress").GetProperty("note").GetString().Should().Be("8 found");
    }

    private static async Task<List<string>> FirstFrameAsync(HttpResponseMessage response)
    {
        using var stream = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
        var lines = new List<string>();
        while (await stream.ReadLineAsync(Ct) is { Length: > 0 } line)
            lines.Add(line);
        return lines;
    }
}
