using Anichron.API.Endpoints;
using Anichron.API.Settings;
using Anichron.API.Tests.Unit.Infrastructure;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Testing.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
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
            Start, Start + Duration.FromSeconds(9), Start + Duration.FromSeconds(9), new WorkProgress(8, 8, "8 found"),
            /*lang=json,strict*/ """{"found": 8}""", null);

    private Task<AuthenticatedTestApp> StartAsync()
        => AuthenticatedTestApp.StartAsync(api => api.MapWorkItemEndpoints(), services =>
        {
            services.AddSingleton(reader);
            services.AddSingleton<TimeProvider>(new FakeTimeProvider());
            services.AddSingleton(Options.Create(new WorkItemEventsSettings()));
            services.AddScoped<WorkItemEvents>();
        });

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
        reader.UpdatedSinceAsync(Arg.Any<Instant>(), Arg.Any<CancellationToken>())
              .Returns(_ => ++polls == 1 ? [Succeeded] : Array.Empty<WorkItemView>());
        await using var timeline = new Timeline();
        await using var app = await StartAsync();
        using var client = app.Client;
        using var request = AuthenticatedTestApp.GetAsAdmin("/api/v1/admin/work-items/events", AdminId);
        request.Headers.Add("Last-Event-ID", "2026-10-18T09:00:05Z");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);

        timeline.Watch(Task.Run(async () => timeline.Record(Describe(await FirstFrameAsync(response))), CancellationToken.None));

        await timeline.ExpectAsync(new Called(
            "event: work-item | id: 2026-10-18T09:00:09Z | aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa 8 found"));
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        await reader.Received(1).UpdatedSinceAsync(Start - Duration.FromSeconds(5), Arg.Any<CancellationToken>());
    }

    private static string Describe(List<string> frame)
    {
        var data = frame.Single(line => line.StartsWith("data: ", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(data["data: ".Length..]);
        var item = $"{json.RootElement.GetProperty("id").GetGuid()} {json.RootElement.GetProperty("progress").GetProperty("note").GetString()}";
        return string.Join(" | ", frame.Where(line => line != data).Order(StringComparer.Ordinal).Append(item));
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
