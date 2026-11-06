using Microsoft.AspNetCore.Http.HttpResults;
using System.Reflection;

namespace Anichron.API.Infrastructure;

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
        => Results.ValidationProblem(FieldErrors(field, code), detail: detail, statusCode: statusCode, extensions: Extensions(code));

    /// <summary>A 400 for one request field, with <paramref name="code"/> also listed under that field in <c>errors</c>.</summary>
    /// <param name="code">The failure's code.</param>
    /// <param name="field">The request field, as the client names it.</param>
    /// <param name="detail">The English text.</param>
    internal static ValidationProblem InvalidField(string code, string field, string detail)
        => TypedResults.ValidationProblem(FieldErrors(field, code), detail: detail, extensions: Extensions(code));

    private static Dictionary<string, string[]> FieldErrors(string field, string code) => new() { [field] = [code] };

    private static Dictionary<string, object?> Extensions(string code) => new() { [CodeExtension] = code };
}

internal static class ErrorCodes
{
    internal static IEnumerable<string> All()
        => typeof(ErrorCodes).GetFields(BindingFlags.Static | BindingFlags.Public)
            .Where(constant => constant.IsLiteral)
            .Select(constant => constant.GetRawConstantValue())
            .OfType<string>()
            .Order(StringComparer.Ordinal);

    public const string InvalidCredentials = "invalid_credentials";
    public const string AccountDisabled = "account_disabled";
    public const string AccountLocked = "account_locked";
    public const string RateLimited = "rate_limited";
    public const string RefreshTokenRequired = "refresh_token_required";
    public const string RefreshTokenInvalid = "refresh_token_invalid";
    public const string InvalidUsername = "invalid_username";
    public const string InvalidEmail = "invalid_email";
    public const string UsernameTaken = "username_taken";
    public const string EmailTaken = "email_taken";
    public const string PasswordTooShort = "password_too_short";
    public const string PasswordTooLong = "password_too_long";
    public const string PasswordPwned = "password_pwned";
    public const string InviteTokenInvalid = "invite_token_invalid";
    public const string PasswordChangeRequired = "password_change_required";
    public const string CannotModifySelf = "cannot_modify_self";
    public const string PathInvalid = "path_invalid";
    public const string PathAlreadyAssigned = "path_already_assigned";
    public const string PathOverlaps = "path_overlaps";
    public const string InvalidDate = "invalid_date";
    public const string InvalidPage = "invalid_page";
    public const string InvalidPageSize = "invalid_page_size";
    public const string PendingOrOutcome = "pending_or_outcome";
    public const string OrphanIdsOrAll = "orphan_ids_or_all";
    public const string InvalidOrphanCount = "invalid_orphan_count";
    public const string UnknownOrphans = "unknown_orphans";
    public const string ServiceUnavailable = "service_unavailable";
}
