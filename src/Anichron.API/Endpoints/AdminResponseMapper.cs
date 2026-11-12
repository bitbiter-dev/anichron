using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Domain;
using Microsoft.Extensions.Options;

namespace Anichron.API.Endpoints;

public interface IAdminResponseMapper
{
    IResult GetCreateUserResult(ServiceResult<AdminCreatedUser, IAdminCreateUserError> result);
    IResult GetResetPasswordResult(AdminUserPasswordReset? result);
    IResult GetUsersResult(List<User> users);
    IResult GetUserResult(User? user);
    IResult GetPatchUserResult(ServiceResult<User, IUpdateUserError> result);
    IResult GetDeleteUserResult(IDeleteUserError? error);
    IResult GetStorageConfigsResult(List<UserStorageConfig>? configs);
    IResult GetCreateStorageConfigResult(ServiceResult<UserStorageConfig, IAddStorageConfigError> result);
    IResult GetDeleteStorageConfigResult(bool deleted);
}

public sealed class AdminResponseMapper(
    IOptions<UsernamePolicy> usernamePolicy,
    IOptions<PasswordPolicy> passwordPolicy) : IAdminResponseMapper
{
    private readonly ServiceProblems problems = new(usernamePolicy.Value, passwordPolicy.Value, ServiceProblems.Fields.Password);

    public IResult GetCreateUserResult(ServiceResult<AdminCreatedUser, IAdminCreateUserError> result)
        => result.Match(
            created => Results.Created(
                ApiPaths.Users.UserLocation(created.Id),
                new AdminCreatedUserResponse(created.Id, created.Username, created.Email, created.TemporaryPassword)),
            error => error.Accept(problems));

    public IResult GetResetPasswordResult(AdminUserPasswordReset? result)
        => result is null
            ? Results.NotFound()
            : Results.Ok(new AdminPasswordResetResponse(result.TemporaryPassword));

    public IResult GetUsersResult(List<User> users)
        => Results.Ok(users.ConvertAll(ToAdminUserResponse));

    public IResult GetUserResult(User? user)
        => user is null ? Results.NotFound() : Results.Ok(ToAdminUserResponse(user));

    public IResult GetPatchUserResult(ServiceResult<User, IUpdateUserError> result)
        => result.Match(user => Results.Ok(ToAdminUserResponse(user)), error => error.Accept(problems));

    public IResult GetDeleteUserResult(IDeleteUserError? error)
        => error is null ? Results.NoContent() : error.Accept(problems);

    public IResult GetStorageConfigsResult(List<UserStorageConfig>? configs)
        => configs is null ? Results.NotFound() : Results.Ok(configs.ConvertAll(ToAdminStorageConfigResponse));

    public IResult GetCreateStorageConfigResult(ServiceResult<UserStorageConfig, IAddStorageConfigError> result)
        => result.Match(
            config => Results.Created(
                ApiPaths.Users.StorageConfigLocation(config.UserId, config.Id), ToAdminStorageConfigResponse(config)),
            error => error.Accept(problems));

    public IResult GetDeleteStorageConfigResult(bool deleted)
        => deleted ? Results.NoContent() : Results.NotFound();

    private static AdminUserResponse ToAdminUserResponse(User user)
        => new(user.Id, user.Username, user.Email, user.IsAdmin, user.IsDisabled, user.StorageConfigs.Count);

    private static AdminStorageConfigResponse ToAdminStorageConfigResponse(UserStorageConfig config)
        => new(config.Id, config.UserId, config.RootPath, config.IsActive);
}
