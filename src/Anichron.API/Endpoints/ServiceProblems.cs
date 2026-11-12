using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.API.Settings;
using static System.Globalization.CultureInfo;

namespace Anichron.API.Endpoints;

/// <summary>The one response for each service error.</summary>
/// <param name="usernamePolicy">States the username rule.</param>
/// <param name="passwordPolicy">States the password rules.</param>
/// <param name="passwordField">The request field a password rejection names.</param>
internal sealed class ServiceProblems(UsernamePolicy usernamePolicy, PasswordPolicy passwordPolicy, string passwordField)
    : IServiceErrorVisitor<IResult>
{
    public IResult Visit(InviteTokenInvalid error)
        => ApiProblems.Problem(StatusCodes.Status422UnprocessableEntity, ErrorCodes.InviteTokenInvalid, ProblemMessages.InviteTokenInvalid);

    public IResult Visit(InvalidUsername error)
        => ApiProblems.FieldProblem(
            StatusCodes.Status422UnprocessableEntity, ErrorCodes.InvalidUsername, Fields.Username, usernamePolicy.InvalidFormatMessage);

    public IResult Visit(InvalidEmail error)
        => ApiProblems.FieldProblem(
            StatusCodes.Status422UnprocessableEntity, ErrorCodes.InvalidEmail, Fields.Email, ProblemMessages.InvalidEmail);

    public IResult Visit(UsernameTaken error)
        => ApiProblems.FieldProblem(StatusCodes.Status409Conflict, ErrorCodes.UsernameTaken, Fields.Username, ProblemMessages.UsernameTaken);

    public IResult Visit(EmailTaken error)
        => ApiProblems.FieldProblem(StatusCodes.Status409Conflict, ErrorCodes.EmailTaken, Fields.Email, ProblemMessages.EmailTaken);

    public IResult Visit(PasswordRejected error)
        => error.Match(
            () => PasswordProblem(ErrorCodes.PasswordTooShort, passwordPolicy.TooShortMessage),
            () => PasswordProblem(ErrorCodes.PasswordTooLong, passwordPolicy.TooLongMessage),
            () => PasswordProblem(ErrorCodes.PasswordPwned, ProblemMessages.PasswordPwned));

    public IResult Visit(InvalidCredentials error)
        => ApiProblems.Problem(StatusCodes.Status401Unauthorized, ErrorCodes.InvalidCredentials, ProblemMessages.InvalidCredentials);

    public IResult Visit(CurrentPasswordWrong error)
        => ApiProblems.FieldProblem(
            StatusCodes.Status400BadRequest, ErrorCodes.InvalidCredentials, Fields.CurrentPassword, ProblemMessages.InvalidCredentials);

    public IResult Visit(AccountDisabled error)
        => ApiProblems.Problem(StatusCodes.Status401Unauthorized, ErrorCodes.AccountDisabled, ProblemMessages.AccountDisabled);

    public IResult Visit(AccountLocked error)
        => new RetryAfter(error.RetryAfterSeconds, ApiProblems.Problem(
            StatusCodes.Status429TooManyRequests, ErrorCodes.AccountLocked, ProblemMessages.AccountTemporarilyLocked(error.RetryAfterSeconds)));

    public IResult Visit(RefreshTokenInvalid error)
        => ApiProblems.Problem(StatusCodes.Status401Unauthorized, ErrorCodes.RefreshTokenInvalid, ProblemMessages.RefreshTokenInvalid);

    public IResult Visit(CannotModifySelf error)
        => ApiProblems.Problem(StatusCodes.Status403Forbidden, ErrorCodes.CannotModifySelf, ProblemMessages.CannotModifySelf);

    public IResult Visit(UserNotFound error) => Results.NotFound();

    public IResult Visit(PathInvalid error)
        => ApiProblems.FieldProblem(StatusCodes.Status422UnprocessableEntity, ErrorCodes.PathInvalid, Fields.RootPath, ProblemMessages.PathInvalid);

    public IResult Visit(PathAlreadyAssigned error)
        => ApiProblems.FieldProblem(StatusCodes.Status409Conflict, ErrorCodes.PathAlreadyAssigned, Fields.RootPath, ProblemMessages.PathAlreadyAssigned);

    public IResult Visit(PathOverlaps error)
        => ApiProblems.FieldProblem(StatusCodes.Status409Conflict, ErrorCodes.PathOverlaps, Fields.RootPath, ProblemMessages.PathOverlaps);

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
