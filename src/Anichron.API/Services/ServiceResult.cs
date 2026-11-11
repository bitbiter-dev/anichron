namespace Anichron.API.Services;

public sealed class ServiceResult<T, TError>
    where TError : class, IServiceError
{
    private readonly T? value;
    private readonly TError? error;

    private ServiceResult(T? value, TError? error)
    {
        this.value = value;
        this.error = error;
    }

    public bool IsSuccess => error is null;

    public static implicit operator ServiceResult<T, TError>(T value)
    {
        return new(value, null);
    }

    public static implicit operator ServiceResult<T, TError>(TError error)
    {
        return new(default, error);
    }

    public TResult Match<TResult>(Func<T, TResult> ok, Func<TError, TResult> failed)
        => error is null ? ok(value!) : failed(error);
}

public static class ServiceResult
{
    public static ServiceResult<T, TError> Fail<T, TError>(TError error)
        where TError : class, IServiceError
        => error;
}
