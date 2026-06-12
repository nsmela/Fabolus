namespace Radaidant.Core.Common;

/// <summary>
/// Represents a strongly-typed domain error, preventing stringly-typed magic values.
/// </summary>
public sealed record Error(string Code, string Description)
{
    /// <summary>
    /// Sentinel value representing the absence of an error. Only valid on a success result.
    /// Never pass this to Result.Failure — that will throw.
    /// </summary>
    public static readonly Error None = new(string.Empty, string.Empty);
}

/// <summary>
/// Encapsulates the outcome of an operation without a return value.
/// </summary>
public class Result
{
    private readonly Error _error;

    /// <summary>
    /// The error associated with a failed result. Do not access on a success result.
    /// </summary>
    public Error Error => IsSuccess
        ? throw new InvalidOperationException("Cannot access Error on a success result. Check IsFailure first.")
        : _error;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    protected Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        _error = error;
    }

    public static Result Success() => new(true, Error.None);
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result Failure(Error error)
    {
        if (error is null)
            throw new ArgumentNullException(nameof(error), "A failure result requires a non-null error.");
        if (error == Error.None)
            throw new ArgumentException("A failure result requires a meaningful error. Use a named error, not Error.None.", nameof(error));

        return new(false, error);
    }

    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>
/// Encapsulates the outcome of an operation that can succeed with a value or fail with a domain error.
/// </summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failed Result. Check IsSuccess first.");

    private Result(T value) : base(true, Error.None) => _value = value;
    private Result(Error error) : base(false, error) => _value = default;

    public static Result<T> Success(T value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value), "A success result requires a non-null value. Use Maybe<T> if the value is genuinely optional.");

        return new(value);
    }

    public new static Result<T> Failure(Error error)
    {
        if (error is null)
            throw new ArgumentNullException(nameof(error), "A failure result requires a non-null error.");
        if (error == Error.None)
            throw new ArgumentException("A failure result requires a meaningful error. Use a named error, not Error.None.", nameof(error));

        return new(error);
    }

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}