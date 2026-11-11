using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.API.Settings;
using static System.Globalization.CultureInfo;

namespace Anichron.API.Endpoints;

/// <summary>The one response for each service error.</summary>
/// <param name="usernamePolicy">States the username rule.</param>
/// <param name="passwordPolicy">States the password rules.</param>
/// <param name="passwordField">The request field a password rejection names.</param>
internal sealed class AuthProblems(UsernamePolicy usernamePolicy, PasswordPolicy passwordPolicy, string passwordField)
    : IServiceErrorVisitor<IResult>
{
    public IResult Visit(InviteTokenInvalid error)
        => ApiProblems.Problem(StatusCodes.Status422UnprocessableEntity, ErrorCodes.InviteTokenInvalid, AuthMessages.InviteTokenInvalid);

    public IResult Visit(InvalidUsername error)
        => ApiProblems.FieldProblem(
            StatusCodes.Status422UnprocessableEntity, ErrorCodes.InvalidUsername, Fields.Username, usernamePolicy.InvalidFormatMessage);

    public IResult Visit(InvalidEmail error)
        => ApiProblems.FieldProblem(
            StatusCodes.Status422UnprocessableEntity, ErrorCodes.InvalidEmail, Fields.Email, AuthMessages.InvalidEmail);

    public IResult Visit(UsernameTaken error)
        => ApiProblems.FieldProblem(StatusCodes.Status409Conflict, ErrorCodes.UsernameTaken, Fields.Username, AuthMessages.UsernameTaken);

    public IResult Visit(EmailTaken error)
        => ApiProblems.FieldProblem(StatusCodes.Status409Conflict, ErrorCodes.EmailTaken, Fields.Email, AuthMessages.EmailTaken);

    public IResult Visit(PasswordRejected error)
        => error.Match(
            () => PasswordProblem(ErrorCodes.PasswordTooShort, passwordPolicy.TooShortMessage),
            () => PasswordProblem(ErrorCodes.PasswordTooLong, passwordPolicy.TooLongMessage),
            () => PasswordProblem(ErrorCodes.PasswordPwned, AuthMessages.PasswordPwned));

    public IResult Visit(InvalidCredentials error)
        => ApiProblems.Problem(StatusCodes.Status401Unauthorized, ErrorCodes.InvalidCredentials, AuthMessages.InvalidCredentials);

    public IResult Visit(CurrentPasswordWrong error)
        => ApiProblems.FieldProblem(
            StatusCodes.Status400BadRequest, ErrorCodes.InvalidCredentials, Fields.CurrentPassword, AuthMessages.InvalidCredentials);

    public IResult Visit(AccountDisabled error)
        => ApiProblems.Problem(StatusCodes.Status401Unauthorized, ErrorCodes.AccountDisabled, AuthMessages.AccountDisabled);

    public IResult Visit(AccountLocked error)
        => new RetryAfter(error.RetryAfterSeconds, ApiProblems.Problem(
            StatusCodes.Status429TooManyRequests, ErrorCodes.AccountLocked, AuthMessages.AccountTemporarilyLocked(error.RetryAfterSeconds)));

    public IResult Visit(RefreshTokenInvalid error)
        => ApiProblems.Problem(StatusCodes.Status401Unauthorized, ErrorCodes.RefreshTokenInvalid, AuthMessages.RefreshTokenInvalid);

    public IResult Visit(CannotModifySelf error)
        => ApiProblems.Problem(StatusCodes.Status403Forbidden, ErrorCodes.CannotModifySelf, AuthMessages.CannotModifySelf);

    public IResult Visit(UserNotFound error) => Results.NotFound();

    public IResult Visit(PathInvalid error)
        => ApiProblems.FieldProblem(StatusCodes.Status422UnprocessableEntity, ErrorCodes.PathInvalid, Fields.RootPath, AuthMessages.PathInvalid);

    public IResult Visit(PathAlreadyAssigned error)
        => ApiProblems.FieldProblem(StatusCodes.Status409Conflict, ErrorCodes.PathAlreadyAssigned, Fields.RootPath, AuthMessages.PathAlreadyAssigned);

    public IResult Visit(PathOverlaps error)
        => ApiProblems.FieldProblem(StatusCodes.Status409Conflict, ErrorCodes.PathOverlaps, Fields.RootPath, AuthMessages.PathOverlaps);

    private IResult PasswordProblem(string code, string detail)
        => ApiProblems.FieldProblem(StatusCodes.Status422UnprocessableEntity, code, passwordField, detail);

    internal static class Fields
    {
        internal const string Username = "username";
        internal const string Email = "email";
        internal const string Password = "password";
        internal const string CurrentPassword = "currentPassword";
        internal const string NewPassword = "newPassword";
        internal const string RootPath = "rootPath";
    }

    private sealed class RetryAfter(int seconds, IResult problem) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.RetryAfter = seconds.ToString(InvariantCulture);
            return problem.ExecuteAsync(httpContext);
        }
    }
}
