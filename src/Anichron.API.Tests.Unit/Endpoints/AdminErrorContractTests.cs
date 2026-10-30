using Anichron.API.Endpoints;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.API.Tests.Unit.Infrastructure;
using Anichron.Core.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using static Anichron.API.Tests.Unit.Endpoints.ProblemAssertions;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class AdminErrorContractTests
{
    private const string UsersPath = "/api/v1/users";

    private static readonly Guid AdminId = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TargetId = new("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly IAuthService auth = Substitute.For<IAuthService>();
    private readonly IAdminUserService adminUsers = Substitute.For<IAdminUserService>();
    private readonly IAdminStorageConfigService adminStorageConfigs = Substitute.For<IAdminStorageConfigService>();

    private Task<AuthenticatedTestApp> StartAsync()
        => AuthenticatedTestApp.StartAsync(
            api => api.MapAdminEndpoints(),
            services =>
            {
                services.AddSingleton(auth);
                services.AddSingleton(adminUsers);
                services.AddSingleton(adminStorageConfigs);
                services.AddSingleton(Substitute.For<IAdminResetService>());
                services.AddSingleton(new AuthCookieSettings { SameSite = SameSiteMode.Strict, RefreshTokenDays = 7 });
                services.AddSingleton(Substitute.For<IClock>());
                services.AddSingleton<IAuthResponseMapper, AuthResponseMapper>();
            });

    private static async Task<HttpResponseMessage> SendAsAdminAsync(
        AuthenticatedTestApp app, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        request.Headers.Add(AuthenticatedTestApp.UserHeader, AdminId.ToString());
        request.Headers.Add(AuthenticatedTestApp.AdminHeader, "true");
        return await app.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(AuthError.InvalidUsername, 422, "invalid_username", "username", "The provided username is not valid.")]
    [InlineData(AuthError.InvalidEmail, 422, "invalid_email", "email", "The provided email address is not valid.")]
    [InlineData(AuthError.UsernameTaken, 409, "username_taken", "username", "Username is already taken.")]
    [InlineData(AuthError.EmailTaken, 409, "email_taken", "email", "An account with this email already exists.")]
    public async Task CreateUser_FieldRejected_IsAValidationProblemOnThatFieldAsync(
        AuthError error, int status, string code, string field, string detail)
    {
        auth.AdminCreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Fail<AdminCreatedUser>(error));
        await using var app = await StartAsync();

        using var response = await SendAsAdminAsync(
            app, HttpMethod.Post, UsersPath, new { username = "alice", email = "alice@example.com" });

        await AssertFieldProblemAsync(response, status, code, field, detail);
    }

    [Fact]
    public async Task PatchUser_Self_Is403CannotModifySelfAsync()
    {
        adminUsers.UpdateAsync(AdminId, TargetId, Arg.Any<bool?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Fail<User>(AuthError.CannotModifySelf));
        await using var app = await StartAsync();

        using var response = await SendAsAdminAsync(app, HttpMethod.Patch, $"{UsersPath}/{TargetId}", new { isAdmin = false });

        await AssertProblemAsync(response, 403, "cannot_modify_self", "You cannot modify your own account.");
    }

    [Fact]
    public async Task DeleteUser_Self_Is403CannotModifySelfAsync()
    {
        adminUsers.DeleteAsync(AdminId, TargetId, Arg.Any<CancellationToken>())
            .Returns(AuthResult.Fail(AuthError.CannotModifySelf));
        await using var app = await StartAsync();

        using var response = await SendAsAdminAsync(app, HttpMethod.Delete, $"{UsersPath}/{TargetId}");

        await AssertProblemAsync(response, 403, "cannot_modify_self", "You cannot modify your own account.");
    }

    [Theory]
    [InlineData(AuthError.PathInvalid, 422, "path_invalid",
        "The root path must be an absolute path to a folder inside /data/originals.")]
    [InlineData(AuthError.PathAlreadyAssigned, 409, "path_already_assigned",
        "This root path is already assigned to a storage config.")]
    [InlineData(AuthError.PathOverlaps, 409, "path_overlaps",
        "The root path contains, or is contained by, an existing storage config.")]
    public async Task CreateStorageConfig_PathRejected_IsAValidationProblemOnRootPathAsync(
        AuthError error, int status, string code, string detail)
    {
        adminStorageConfigs.AddAsync(TargetId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Fail<UserStorageConfig>(error));
        await using var app = await StartAsync();

        using var response = await SendAsAdminAsync(
            app, HttpMethod.Post, $"{UsersPath}/{TargetId}/storage-configs", new { rootPath = "/data/originals/alice" });

        await AssertFieldProblemAsync(response, status, code, "rootPath", detail);
    }
}
