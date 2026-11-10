using Anichron.API.Services;
using Xunit.Sdk;

namespace Anichron.API.Tests.Unit.Services;

internal static class ServiceResults
{
    public static T Value<T, TError>(this ServiceResult<T, TError> result)
        where TError : class, IServiceError
        => result.Match(value => value, error => throw new XunitException($"Expected success, got {error}."));

    public static TError Error<T, TError>(this ServiceResult<T, TError> result)
        where TError : class, IServiceError
        => result.Match(value => throw new XunitException($"Expected failure, got {value}."), error => error);

    public static TError NewError<TError>(Type errorType)
        where TError : IServiceError
        => (TError)Activator.CreateInstance(errorType)!;
}
