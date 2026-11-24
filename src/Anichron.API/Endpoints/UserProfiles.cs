using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;

namespace Anichron.API.Endpoints;

public interface IUserProfiles
{
    Task<UserProfileResponse> OfAsync(User user, CancellationToken ct);
}

public sealed class UserProfiles(IUserStorageConfigRepository storageConfigs) : IUserProfiles
{
    public async Task<UserProfileResponse> OfAsync(User user, CancellationToken ct)
        => new(user.Id, user.Username, user.Email, user.IsAdmin, user.MustChangePassword,
            await storageConfigs.AnyActiveByUserIdAsync(user.Id, ct));
}
