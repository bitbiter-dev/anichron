namespace Anichron.API.Services;

public enum AuthError
{
    None = 0,
    UsernameTaken = 1,
    EmailTaken = 2,
    InvalidCredentials = 3,
    TokenInvalid = 4,
    InvalidUsername = 5,
    InvalidEmail = 6,
    PasswordTooShort = 7,
    PasswordTooLong = 8,
    PasswordPwned = 9,
    AccountDisabled = 10,
    AccountTemporarilyLocked = 11,
    InviteTokenInvalid = 12,
    CannotModifySelf = 13,
    UserNotFound = 14,
    PathAlreadyAssigned = 15,
    StorageConfigNotFound = 16,
}
