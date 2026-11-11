using Anichron.OpenApiGenerator;
using Microsoft.OpenApi;
using System.Text.RegularExpressions;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed partial class ApiDocumentTests(RouteTableTests.RouteTable table, ApiDocumentTests.Document composed)
    : IClassFixture<RouteTableTests.RouteTable>, IClassFixture<ApiDocumentTests.Document>
{
    public sealed class Document : IAsyncLifetime
    {
        internal OpenApiDocument Value { get; private set; } = null!;

        public async ValueTask InitializeAsync() => Value = await ApiDocument.ComposeAsync();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [GeneratedRegex(@"\{(\w+):[^}]+\}")]
    private static partial Regex RouteConstraint();

    private const string Problem = "application/problem+json ProblemDetails";
    private const string Invalid = "application/problem+json HttpValidationProblemDetails";

    private static readonly string[] User = [$"401 {Problem}", $"403 {Problem}"];
    private static readonly string[] Body = [$"400 {Problem}", $"415 {Problem}"];
    private static readonly string[] RateLimited = [$"429 {Problem}"];
    private const string Unavailable = $"503 {Problem}";

    private static string[] Of(params IEnumerable<string>[] parts) => [.. parts.SelectMany(part => part)];

    public static TheoryData<string, string[]> Responses => new()
    {
        { "POST /api/v1/auth/register", Of(["200 application/json SessionResponse", $"409 {Invalid}", $"422 {Invalid}"], Body, RateLimited) },
        { "POST /api/v1/auth/login", Of(["200 application/json SessionResponse", $"401 {Problem}"], Body, RateLimited) },
        { "POST /api/v1/auth/login/mobile", Of(["200 application/json SessionResponse", $"401 {Problem}"], Body, RateLimited) },
        { "POST /api/v1/auth/refresh", Of(["200 application/json SessionResponse", $"401 {Problem}"], Body, RateLimited) },
        { "POST /api/v1/auth/logout", Of(["204", $"401 {Problem}"], Body) },
        { "POST /api/v1/auth/password-reset/request", Of([$"501 {Problem}"], RateLimited) },
        { "POST /api/v1/auth/password-reset/confirm", Of([$"501 {Problem}"], RateLimited) },
        { "GET /api/v1/users/me", ["200 application/json UserProfileResponse", $"401 {Problem}", $"404 {Problem}"] },
        { "POST /api/v1/users/me/password", ["200 application/json SessionResponse", $"400 {Invalid}", $"401 {Problem}", $"415 {Problem}", $"422 {Invalid}", $"429 {Problem}"] },
        { "POST /api/v1/users", Of(["201 application/json AdminCreatedUserResponse", $"409 {Invalid}", $"422 {Invalid}"], User, Body, RateLimited) },
        { "GET /api/v1/users", Of(["200 application/json AdminUserResponse[]"], User) },
        { "GET /api/v1/users/{userId}", Of(["200 application/json AdminUserResponse", $"404 {Problem}"], User) },
        { "PATCH /api/v1/users/{userId}", Of(["200 application/json AdminUserResponse", $"404 {Problem}"], User, Body, RateLimited) },
        { "DELETE /api/v1/users/{userId}", Of(["204", $"404 {Problem}"], User, RateLimited) },
        { "POST /api/v1/users/{userId}/password-reset", Of(["200 application/json AdminPasswordResetResponse", $"404 {Problem}"], User, RateLimited) },
        { "GET /api/v1/users/{userId}/storage-configs", Of(["200 application/json AdminStorageConfigResponse[]", $"404 {Problem}"], User) },
        { "POST /api/v1/users/{userId}/storage-configs", Of(["201 application/json AdminStorageConfigResponse", $"404 {Problem}", $"409 {Invalid}", $"422 {Invalid}"], User, Body, RateLimited) },
        { "DELETE /api/v1/users/{userId}/storage-configs/{configId}", Of(["204", $"404 {Problem}"], User, RateLimited) },
        { "GET /api/v1/proxies/{assetId}/{type}", Of(["200 image/jpeg Stream", "200 video/mp4 Stream", "206 image/jpeg Stream", "206 video/mp4 Stream", "304", $"404 {Problem}", $"412 {Problem}", "416"], User) },
        { "PATCH /api/v1/assets/{assetId}/interaction", Of(["200 application/json AssetInteractionDto", $"404 {Problem}"], User, Body) },
        { "GET /api/v1/flashbacks/on-this-day", Of(["200 application/json OnThisDayDto", $"400 {Invalid}"], User) },
        { "GET /api/v1/bursts/{burstId}/assets", Of(["200 application/json AssetDto[]", $"404 {Problem}"], User) },
        { "GET /api/v1/admin/work-items", Of(["200 application/json WorkItemPageDto", $"400 {Invalid}"], User) },
        { "GET /api/v1/admin/work-items/events", Of(["200 text/event-stream SseItemOfWorkItemDto"], User) },
        { "GET /api/v1/admin/work-items/{workItemId}", Of(["200 application/json WorkItemDto", $"404 {Problem}"], User) },
        { "POST /api/v1/admin/work-items/{workItemId}/cancel", Of(["200 application/json WorkItemDto", "202 application/json WorkItemDto", $"404 {Problem}", "409 application/json WorkItemDto"], User) },
        { "POST /api/v1/admin/proxy-sweeps", Of(["202 application/json ProxySweepRequestedDto"], User) },
        { "GET /api/v1/admin/proxy-sweeps/{sweepId}/orphans", Of(["200 application/json ProxyOrphanPageDto", $"400 {Invalid}", $"404 {Problem}", $"409 {Problem}"], User) },
        { "POST /api/v1/admin/proxy-sweeps/{sweepId}/deletions", Of(["202 application/json ProxyDeletionsRequestedDto", $"400 {Invalid}", $"404 {Problem}", $"409 {Problem}", $"415 {Problem}"], User) },
    };

    [Theory]
    [MemberData(nameof(Responses))]
    public void Operation_DescribesEveryResponseItCanGive(string operation, string[] expected)
    {
        var (method, path) = (operation.Split(' ')[0], operation.Split(' ')[1]);
        var described = composed.Value.Paths[path].Operations![new HttpMethod(method)].Responses!
            .SelectMany(response => response.Value.Content is { Count: > 0 } content
                ? content.Select(media => $"{response.Key} {media.Key} {SchemaName(media.Value.Schema)}".TrimEnd())
                : [response.Key]);

        described.Should().BeEquivalentTo([.. expected, Unavailable]);
    }

    [Fact]
    public void Responses_CoverEveryOperation()
        => Responses.Select(row => row.Data.Item1).Should().HaveCount(OperationCount());

    [Fact]
    public void ProblemSchemas_CarryTheErrorCode()
    {
        foreach (var name in new[] { "ProblemDetails", "HttpValidationProblemDetails" })
        {
            var properties = composed.Value.Components!.Schemas![name].Properties!;
            properties.Should().ContainKey("code").WhoseValue.Should().BeOfType<OpenApiSchemaReference>()
                .Which.Reference.Id.Should().Be("ErrorCode");
            properties.Should().ContainKey("detail");
        }
    }

    [Fact]
    public void FieldErrors_AreErrorCodes()
    {
        var errors = composed.Value.Components!.Schemas!["HttpValidationProblemDetails"].Properties!["errors"];

        SchemaName(errors.AdditionalProperties).Should().Be("ErrorCode[]");
    }

    [Fact]
    public void ErrorCode_ListsEveryCodeTheApiGives()
    {
        var errorCode = composed.Value.Components!.Schemas!["ErrorCode"];

        errorCode.Type.Should().Be(JsonSchemaType.String);
        errorCode.Enum!.Select(value => value.GetValue<string>()).Should().Equal(
            "account_disabled",
            "account_locked",
            "cannot_modify_self",
            "email_taken",
            "invalid_credentials",
            "invalid_date",
            "invalid_email",
            "invalid_orphan_count",
            "invalid_page",
            "invalid_page_size",
            "invalid_username",
            "invite_token_invalid",
            "orphan_ids_or_all",
            "password_change_required",
            "password_pwned",
            "password_too_long",
            "password_too_short",
            "path_already_assigned",
            "path_invalid",
            "path_overlaps",
            "pending_or_outcome",
            "rate_limited",
            "refresh_token_invalid",
            "refresh_token_required",
            "service_unavailable",
            "unknown_orphans",
            "username_taken");
    }

    [Fact]
    public void Document_DescribesEveryRouteButHealthz()
    {
        var described = composed.Value.Paths
            .SelectMany(path => path.Value.Operations!.Keys.Select(method => $"{method.Method} {path.Key}"));
        var mapped = table.Routes
            .Where(route => route.Pattern != "/api/v1/healthz")
            .Select(route => $"{route.Method} {RouteConstraint().Replace(route.Pattern, "{$1}")}");

        described.Should().BeEquivalentTo(mapped);
    }

    [Fact]
    public void Numbers_AreNeverStrings()
    {
        var schemas = composed.Value.Components!.Schemas!.Values
            .SelectMany(schema => schema.Properties?.Values ?? [])
            .Concat(composed.Value.Paths.Values
                .SelectMany(path => path.Operations!.Values)
                .SelectMany(operation => operation.Parameters ?? [])
                .Select(parameter => parameter.Schema!))
            .Where(schema => schema.Format is "int32" or "int64" or "double")
            .ToList();

        schemas.Should().NotBeEmpty();
        schemas.Should().AllSatisfy(schema => schema.Type!.Value.HasFlag(JsonSchemaType.String).Should().BeFalse());
    }

    [Fact]
    public void Document_IsTitledAfterTheApi()
        => composed.Value.Info.Title.Should().Be("Anichron.API | v1");

    private int OperationCount() => composed.Value.Paths.Sum(path => path.Value.Operations!.Count);

    private static string SchemaName(IOpenApiSchema? schema)
        => schema switch
        {
            OpenApiSchemaReference reference => reference.Reference.Id!,
            { Type: JsonSchemaType.Array, Items: { } items } => SchemaName(items) + "[]",
            _ => string.Empty,
        };
}
