using Anichron.API.Endpoints;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class AdminResponseMapperTests
{
    private static AdminResponseMapper CreateTestee()
        => new(Options.Create(new UsernamePolicy()), Options.Create(new PasswordPolicy()));

    // ==========================================================================
    // GetCreateUserResult
    // ==========================================================================

    [Fact]
    public void GetCreateUserResult_Success_Returns201WithLocationAndBody()
    {
        var userId = Guid.NewGuid();
        var created = new AdminCreatedUser(userId, "alice", "alice@example.com", "TempPass==");
        var testee = CreateTestee();

        var result = testee.GetCreateUserResult(created);

        var ok = result.Should()
            .BeOfType<Microsoft.AspNetCore.Http.HttpResults.Created<AdminCreatedUserResponse>>().Subject;
        ok.StatusCode.Should().Be(201);
        ok.Location.Should().Contain(userId.ToString());
        ok.Value.Should().Be(new AdminCreatedUserResponse(userId, "alice", "alice@example.com", "TempPass=="));
    }

    // ==========================================================================
    // GetResetPasswordResult
    // ==========================================================================

    [Fact]
    public void GetResetPasswordResult_NullResult_Returns404()
    {
        var testee = CreateTestee();

        var result = testee.GetResetPasswordResult(null);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public void GetResetPasswordResult_Success_Returns200WithTemporaryPassword()
    {
        var testee = CreateTestee();

        var result = testee.GetResetPasswordResult(new AdminUserPasswordReset("TempPass=="));

        var ok = result.Should()
            .BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<AdminPasswordResetResponse>>().Subject;
        ok.StatusCode.Should().Be(200);
        ok.Value.Should().Be(new AdminPasswordResetResponse("TempPass=="));
    }

    // ==========================================================================
    // GetUsersResult
    // ==========================================================================

    [Fact]
    public void GetUsersResult_EmptyList_Returns200WithEmptyCollection()
    {
        var testee = CreateTestee();

        var result = testee.GetUsersResult([]);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public void GetUsersResult_WithUsers_Returns200WithMappedResponses()
    {
        var testee = CreateTestee();
        var userId = new Guid("11111111-0000-0000-0000-000000000001");
        var user = new User
        {
            Id = userId,
            Username = "alice",
            Email = "alice@example.com",
            IsAdmin = true,
            IsDisabled = false,
            StorageConfigs = [new() { Id = Guid.NewGuid() }, new() { Id = Guid.NewGuid() }],
        };

        var ok = testee.GetUsersResult([user])
            .Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<AdminUserResponse>>>().Subject;

        ok.StatusCode.Should().Be(200);
        ok.Value.Should().ContainSingle(r =>
            r.Id == userId &&
            r.Username == "alice" &&
            r.Email == "alice@example.com" &&
            r.IsAdmin &&
            !r.IsDisabled &&
            r.StorageConfigCount == 2);
    }

    // ==========================================================================
    // GetUserResult
    // ==========================================================================

    [Fact]
    public void GetUserResult_NullUser_Returns404()
    {
        var testee = CreateTestee();

        var result = testee.GetUserResult(null);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public void GetUserResult_UserFound_Returns200WithMappedResponse()
    {
        var testee = CreateTestee();
        var userId = new Guid("22222222-0000-0000-0000-000000000002");
        var user = new User
        {
            Id = userId,
            Username = "bob",
            Email = "bob@example.com",
            IsAdmin = false,
            IsDisabled = true,
            StorageConfigs = [new() { Id = Guid.NewGuid() }],
        };

        var ok = testee.GetUserResult(user)
            .Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<AdminUserResponse>>().Subject;

        ok.StatusCode.Should().Be(200);
        ok.Value.Should().Be(new AdminUserResponse(userId, "bob", "bob@example.com", false, true, 1));
    }

    // ==========================================================================
    // GetPatchUserResult
    // ==========================================================================

    [Fact]
    public void GetPatchUserResult_Success_Returns200WithMappedUser()
    {
        var testee = CreateTestee();
        var userId = new Guid("33333333-0000-0000-0000-000000000003");
        var user = new User
        {
            Id = userId,
            Username = "carol",
            Email = "carol@example.com",
            IsAdmin = true,
            IsDisabled = false,
            StorageConfigs = [],
        };

        var ok = testee.GetPatchUserResult(user)
            .Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<AdminUserResponse>>().Subject;

        ok.StatusCode.Should().Be(200);
        ok.Value.Should().Be(new AdminUserResponse(userId, "carol", "carol@example.com", true, false, 0));
    }

    // ==========================================================================
    // GetDeleteUserResult
    // ==========================================================================

    [Fact]
    public void GetDeleteUserResult_Success_Returns204()
    {
        var testee = CreateTestee();

        var result = testee.GetDeleteUserResult(null);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(204);
    }

    // ==========================================================================
    // GetStorageConfigsResult
    // ==========================================================================

    [Fact]
    public void GetStorageConfigsResult_Success_ReturnsMappedList()
    {
        var testee = CreateTestee();
        var userId = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        var configId = new Guid("bbbbbbbb-0000-0000-0000-000000000001");
        var configs = new List<UserStorageConfig>
        {
            new() { Id = configId, UserId = userId, RootPath = "/nas/photos", IsActive = true },
        };

        var ok = testee.GetStorageConfigsResult(configs)
            .Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<AdminStorageConfigResponse>>>().Subject;

        ok.StatusCode.Should().Be(200);
        ok.Value.Should().ContainSingle(c => c == new AdminStorageConfigResponse(configId, userId, "/nas/photos", true));
    }

    [Fact]
    public void GetStorageConfigsResult_NoUser_Returns404()
    {
        var testee = CreateTestee();

        var result = testee.GetStorageConfigsResult(null);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(404);
    }

    // ==========================================================================
    // GetCreateStorageConfigResult
    // ==========================================================================

    [Fact]
    public void GetCreateStorageConfigResult_Success_Returns201WithLocationAndMappedBody()
    {
        var testee = CreateTestee();
        var userId = new Guid("aaaaaaaa-0000-0000-0000-000000000002");
        var configId = new Guid("bbbbbbbb-0000-0000-0000-000000000002");
        var config = new UserStorageConfig
        {
            Id = configId,
            UserId = userId,
            RootPath = "/nas/photos",
            IsActive = true,
        };

        var created = testee.GetCreateStorageConfigResult(config)
            .Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Created<AdminStorageConfigResponse>>().Subject;

        created.StatusCode.Should().Be(201);
        created.Location.Should().Be($"/api/v1/users/{userId}/storage-configs/{configId}");
        created.Value.Should().Be(new AdminStorageConfigResponse(configId, userId, "/nas/photos", true));
    }

    // ==========================================================================
    // GetDeleteStorageConfigResult
    // ==========================================================================

    [Fact]
    public void GetDeleteStorageConfigResult_Success_Returns204()
    {
        var testee = CreateTestee();

        var result = testee.GetDeleteStorageConfigResult(deleted: true);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(204);
    }

    [Fact]
    public void GetDeleteStorageConfigResult_NotDeleted_Returns404()
    {
        var testee = CreateTestee();

        var result = testee.GetDeleteStorageConfigResult(deleted: false);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(404);
    }
}
