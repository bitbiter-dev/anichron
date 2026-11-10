namespace Anichron.API.Services;

public interface IServiceError
{
    TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor);
}

public interface IServiceErrorVisitor<out TResult>
{
    TResult Visit(InviteTokenInvalid error);
    TResult Visit(InvalidUsername error);
    TResult Visit(InvalidEmail error);
    TResult Visit(UsernameTaken error);
    TResult Visit(EmailTaken error);
    TResult Visit(PasswordRejected error);
    TResult Visit(InvalidCredentials error);
    TResult Visit(CurrentPasswordWrong error);
    TResult Visit(AccountDisabled error);
    TResult Visit(AccountLocked error);
    TResult Visit(RefreshTokenInvalid error);
    TResult Visit(CannotModifySelf error);
    TResult Visit(UserNotFound error);
    TResult Visit(PathInvalid error);
    TResult Visit(PathAlreadyAssigned error);
    TResult Visit(PathOverlaps error);
}

public interface IRegisterError : IServiceError;

public interface ILoginError : IServiceError;

public interface IRefreshError : IServiceError;

public interface IChangePasswordError : IServiceError;

public interface IAdminCreateUserError : IServiceError;

public interface IUpdateUserError : IServiceError;

public interface IDeleteUserError : IServiceError;

public interface IAddStorageConfigError : IServiceError;

public interface IIdentityRejection : IRegisterError, IAdminCreateUserError;

public sealed record InviteTokenInvalid : IRegisterError
{
    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);
}

public sealed record InvalidUsername : IIdentityRejection
{
    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);
}

public sealed record InvalidEmail : IIdentityRejection
{
    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);
}

public sealed record UsernameTaken : IIdentityRejection
{
    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);
}

public sealed record EmailTaken : IIdentityRejection
{
    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);
}

public abstract record PasswordRejected : IRegisterError, IChangePasswordError
{
    private protected PasswordRejected()
    {
    }

    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);

    public abstract TResult Match<TResult>(Func<TResult> tooShort, Func<TResult> tooLong, Func<TResult> pwned);
}

public sealed record PasswordTooShort : PasswordRejected
{
    public override TResult Match<TResult>(Func<TResult> tooShort, Func<TResult> tooLong, Func<TResult> pwned) => tooShort();
}

public sealed record PasswordTooLong : PasswordRejected
{
    public override TResult Match<TResult>(Func<TResult> tooShort, Func<TResult> tooLong, Func<TResult> pwned) => tooLong();
}

public sealed record PasswordPwned : PasswordRejected
{
    public override TResult Match<TResult>(Func<TResult> tooShort, Func<TResult> tooLong, Func<TResult> pwned) => pwned();
}

public sealed record InvalidCredentials : ILoginError
{
    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);
}

public sealed record CurrentPasswordWrong : IChangePasswordError
{
    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);
}

public sealed record AccountDisabled : ILoginError, IRefreshError
{
    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);
}

public sealed record AccountLocked(int RetryAfterSeconds) : ILoginError, IRefreshError
{
    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);
}

public sealed record RefreshTokenInvalid : IRefreshError
{
    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);
}

public sealed record CannotModifySelf : IUpdateUserError, IDeleteUserError
{
    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);
}

public sealed record UserNotFound : IUpdateUserError, IDeleteUserError, IAddStorageConfigError
{
    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);
}

public sealed record PathInvalid : IAddStorageConfigError
{
    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);
}

public sealed record PathAlreadyAssigned : IAddStorageConfigError
{
    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);
}

public sealed record PathOverlaps : IAddStorageConfigError
{
    public TResult Accept<TResult>(IServiceErrorVisitor<TResult> visitor) => visitor.Visit(this);
}
