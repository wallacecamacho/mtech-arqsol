namespace CashFlow.SharedKernel.Application;

public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }

    protected Result(bool isSuccess, string? error)
    {
        if (isSuccess && error is not null)
            throw new InvalidOperationException("Successful result cannot have an error.");
        if (!isSuccess && error is null)
            throw new InvalidOperationException("Failure result must have an error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);
    public static Result<T> Success<T>(T value) => new(value, true, null);
    public static Result<T> Failure<T>(string error) => new(default!, false, error);
}

public class Result<T> : Result
{
    public T Value { get; }

    internal Result(T value, bool isSuccess, string? error) : base(isSuccess, error)
    {
        Value = value;
    }
}
