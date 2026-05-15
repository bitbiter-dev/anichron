namespace Anichron.Core.Data;

// Postgres lowercases unquoted identifiers, so these are the runtime names returned
// by PostgresException.ConstraintName. EF Core auto-generates the corresponding
// index names (IX_Users_Email, IX_Users_Username) which Postgres stores as these values.
public static class UserIndexNames
{
    public const string EmailUnique = "ix_users_email";
    public const string UsernameUnique = "ix_users_username";
}
