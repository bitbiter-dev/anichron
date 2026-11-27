using Anichron.API.Endpoints;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.API.Tests.Unit.Infrastructure;
using Anichron.Core.Data.Repository;
using Anichron.Core.Work;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using static Anichron.API.Tests.Unit.Endpoints.ProblemAssertions;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class RequestBodyBindingTests
{
    private static readonly Guid CallerId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OtherId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly IAuthService auth = Substitute.For<IAuthService>();
    private readonly IAdminUserService adminUsers = Substitute.For<IAdminUserService>();
    private readonly IAdminStorageConfigService storageConfigs = Substitute.For<IAdminStorageConfigService>();
    private readonly IAssetInteractionWriter interactions = Substitute.For<IAssetInteractionWriter>();
    private readonly IWorkItemReader workItems = Substitute.For<IWorkItemReader>();
    private readonly IWorkQueue queue = Substitute.For<IWorkQueue>();
    private readonly ISoftDeletedAssetReader softDeleted = Substitute.For<ISoftDeletedAssetReader>();

    private Task<AuthenticatedTestApp> StartAsync()
        => AuthenticatedTestApp.StartAsync(
            api =>
            {
                api.MapAuthEndpoints();
                api.MapUserEndpoints();
                api.MapAdminEndpoints();
                api.MapMediaEndpoints();
                api.MapProxySweepEndpoints();
            },
            services =>
            {
                services.AddSingleton(auth);
                services.AddSingleton(adminUsers);
                services.AddSingleton(storageConfigs);
                services.AddSingleton(interactions);
                services.AddSingleton(Substitute.For<IAssetViewReader>());
                services.AddSingleton(softDeleted);
                services.AddSingleton(workItems);
                services.AddSingleton(Substitute.For<IProxyOrphanReader>());
                services.AddSingleton(queue);
                services.AddSingleton(new AuthCookieSettings { SameSite = SameSiteMode.Strict, RefreshTokenDays = 7 });
                services.AddSingleton(Substitute.For<IClock>());
                services.AddSingleton<ISessionResponseMapper, SessionResponseMapper>();
            });

    private static async Task<HttpResponseMessage> SendAsync(AuthenticatedTestApp app, HttpMethod method, string path, string json)
    {
        using var request = new HttpRequestMessage(method, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add(AuthenticatedTestApp.UserHeader, CallerId.ToString());
        request.Headers.Add(AuthenticatedTestApp.AdminHeader, "true");
        return await app.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static TheoryData<string, string, string> BodiesMissingARequiredField => new()
    {
        { "POST", "/api/v1/auth/login", /*lang=json,strict*/ """{"password":"SecurePass1!"}""" },
        { "POST", "/api/v1/auth/login", /*lang=json,strict*/ """{"usernameOrEmail":"alice"}""" },
        { "POST", "/api/v1/auth/login", /*lang=json,strict*/ """{"usernameOrEmail":null,"password":"SecurePass1!"}""" },
        { "POST", "/api/v1/auth/login", /*lang=json,strict*/ """{"usernameOrEmail":"alice","password":null}""" },
        { "POST", "/api/v1/auth/login/mobile", /*lang=json,strict*/ """{"password":"SecurePass1!"}""" },
        { "POST", "/api/v1/auth/login/mobile", /*lang=json,strict*/ """{"usernameOrEmail":"alice"}""" },
        { "POST", "/api/v1/auth/login/mobile", /*lang=json,strict*/ """{"usernameOrEmail":null,"password":"SecurePass1!"}""" },
        { "POST", "/api/v1/auth/login/mobile", /*lang=json,strict*/ """{"usernameOrEmail":"alice","password":null}""" },
        { "POST", "/api/v1/auth/register", /*lang=json,strict*/ """{"username":"alice","email":"alice@example.com","password":"SecurePass1!"}""" },
        { "POST", "/api/v1/auth/register", /*lang=json,strict*/ """{"username":"alice","email":null,"password":"SecurePass1!","inviteToken":"inv"}""" },
        { "POST", "/api/v1/auth/refresh", /*lang=json,strict*/ "{}" },
        { "POST", "/api/v1/auth/refresh", /*lang=json,strict*/ """{"refreshToken":null}""" },
        { "POST", "/api/v1/auth/logout", /*lang=json,strict*/ "{}" },
        { "POST", "/api/v1/users/me/password", /*lang=json,strict*/ """{"currentPassword":"OldPass1!"}""" },
        { "POST", "/api/v1/users/me/password", /*lang=json,strict*/ """{"currentPassword":"OldPass1!","newPassword":null}""" },
        { "POST", "/api/v1/users", /*lang=json,strict*/ """{"username":"alice"}""" },
        { "POST", "/api/v1/users", /*lang=json,strict*/ """{"username":"alice","email":null}""" },
        { "POST", $"/api/v1/users/{OtherId}/storage-configs", /*lang=json,strict*/ "{}" },
        { "POST", $"/api/v1/users/{OtherId}/storage-configs", /*lang=json,strict*/ """{"rootPath":null}""" },
        { "POST", "/api/v1/assets/purges", /*lang=json,strict*/ "{}" },
        { "POST", "/api/v1/assets/purges", /*lang=json,strict*/ """{"assetIds":null}""" },
    };

    [Theory]
    [MemberData(nameof(BodiesMissingARequiredField))]
    public async Task Body_MissingOrNullRequiredField_IsABadRequestProblemAndReachesNoServiceAsync(string method, string path, string json)
    {
        await using var app = await StartAsync();

        using var response = await SendAsync(app, new HttpMethod(method), path, json);

        await AssertStatusProblemAsync(response, 400);
        auth.ReceivedCalls().Should().BeEmpty();
        adminUsers.ReceivedCalls().Should().BeEmpty();
        storageConfigs.ReceivedCalls().Should().BeEmpty();
        softDeleted.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task PatchInteraction_OnlyStarred_IsAppliedAsync()
    {
        interactions.ApplyAsync(CallerId, OtherId, new InteractionChange(true, null, null), Arg.Any<CancellationToken>())
            .Returns(new InteractionState(Starred: true, Liked: false, Hidden: false));
        await using var app = await StartAsync();

        using var response = await SendAsync(app, HttpMethod.Patch, $"/api/v1/assets/{OtherId}/interaction", /*lang=json,strict*/ """{"starred":true}""");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await interactions.Received(1).ApplyAsync(CallerId, OtherId, new InteractionChange(true, null, null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchAdminUser_OnlyIsDisabled_IsAppliedAsync()
    {
        await using var app = await StartAsync();

        using var response = await SendAsync(app, HttpMethod.Patch, $"/api/v1/users/{OtherId}", /*lang=json,strict*/ """{"isDisabled":true}""");

        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
        await adminUsers.Received(1).UpdateAsync(CallerId, OtherId, null, true, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{"orphanIds":["00000000-0000-0000-0000-000000000001"]}""")]
    [InlineData(/*lang=json,strict*/ """{"all":true}""")]
    public async Task ProxyDeletions_EitherFieldAlone_IsAcceptedAsync(string json)
    {
        var sweepId = Guid.Parse("50000000-0000-0000-0000-000000000000");
        await using var app = await StartAsync();

        using var response = await SendAsync(app, HttpMethod.Post, $"/api/v1/admin/proxy-sweeps/{sweepId}/deletions", json);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await workItems.Received(1).FindAsync(sweepId, Arg.Any<CancellationToken>());
    }
}
