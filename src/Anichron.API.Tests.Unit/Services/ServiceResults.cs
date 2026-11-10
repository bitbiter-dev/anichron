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

    /// <summary>A new error of <paramref name="errorType"/>, which theory data names because an error is not serializable.</summary>
    /// <typeparam name="TError">The operation's error type.</typeparam>
    /// <param name="errorType">A concrete error type with a parameterless constructor.</param>
    public static TError NewError<TError>(Type errorType)
        where TError : IServiceError
        => (TError)Activator.CreateInstance(errorType)!;
}
