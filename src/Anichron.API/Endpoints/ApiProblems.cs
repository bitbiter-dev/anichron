namespace Anichron.API.Endpoints;

/// <summary>Failures as Problem Details: a stable <c>code</c> extension, and the English text in <c>detail</c>.</summary>
internal static class ApiProblems
{
    internal const string CodeExtension = "code";

    internal static IResult Problem(int statusCode, string code, string detail)
        => Results.Problem(detail: detail, statusCode: statusCode, extensions: Extensions(code));

    /// <summary>A failure of one request field, with <paramref name="code"/> also listed under that field in <c>errors</c>.</summary>
    /// <param name="statusCode">The response status.</param>
    /// <param name="code">The failure's code.</param>
    /// <param name="field">The request field, as the client names it in JSON.</param>
    /// <param name="detail">The English text.</param>
    internal static IResult FieldProblem(int statusCode, string code, string field, string detail)
        => Results.ValidationProblem(
            errors: new Dictionary<string, string[]> { [field] = [code] },
            detail: detail,
            statusCode: statusCode,
            extensions: Extensions(code));

    private static Dictionary<string, object?> Extensions(string code) => new() { [CodeExtension] = code };
}

internal static class ErrorCodes
{
    internal const string InvalidCredentials = "invalid_credentials";
    internal const string AccountDisabled = "account_disabled";
    internal const string AccountLocked = "account_locked";
    internal const string RateLimited = "rate_limited";
    internal const string RefreshTokenRequired = "refresh_token_required";
    internal const string RefreshTokenInvalid = "refresh_token_invalid";
    internal const string InvalidUsername = "invalid_username";
    internal const string InvalidEmail = "invalid_email";
    internal const string UsernameTaken = "username_taken";
    internal const string EmailTaken = "email_taken";
    internal const string PasswordTooShort = "password_too_short";
    internal const string PasswordTooLong = "password_too_long";
    internal const string PasswordPwned = "password_pwned";
    internal const string InviteTokenInvalid = "invite_token_invalid";
    internal const string PasswordChangeRequired = "password_change_required";
    internal const string CannotModifySelf = "cannot_modify_self";
    internal const string PathInvalid = "path_invalid";
    internal const string PathAlreadyAssigned = "path_already_assigned";
    internal const string PathOverlaps = "path_overlaps";
    internal const string InvalidDate = "invalid_date";
}
