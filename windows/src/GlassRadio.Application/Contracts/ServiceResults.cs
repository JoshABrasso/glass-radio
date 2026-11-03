namespace GlassRadio.Application.Contracts;

public sealed record ServiceError(string Code, string Message, Exception? Exception = null);

public sealed record ServiceResult<T>(T? Value, ServiceError? Error, bool IsStaleFallback = false)
{
    public bool IsSuccess => Error is null;
    public static ServiceResult<T> Success(T value, bool isStaleFallback = false) => new(value, null, isStaleFallback);
    public static ServiceResult<T> Failure(string code, string message, Exception? exception = null) => new(default, new ServiceError(code, message, exception));
}
