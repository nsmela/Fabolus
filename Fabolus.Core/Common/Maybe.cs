namespace Radaidant.Core.Common;

/// <summary>
/// Represents an optional value. Use when a value is genuinely optional and its absence
/// is not an error. Do not use it to smuggle failures — that is Result's job.
/// </summary>
public readonly struct Maybe<T>
{
    private readonly T _value;

    public bool HasValue { get; }
    public bool HasNoValue => !HasValue;

    private Maybe(T value)
    {
        _value = value;
        HasValue = true;
    }

    /// <summary>
    /// Creates a Maybe containing the given value.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when value is null. Use Maybe{T}.None() instead.</exception>
    public static Maybe<T> Some(T value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value), "Use Maybe<T>.None() instead of passing null.");

        return new(value);
    }

    /// <summary>
    /// Returns an empty Maybe (HasValue = false).
    /// </summary>
    public static Maybe<T> None() => default;

    /// <summary>
    /// Returns the contained value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if HasValue is false. Always check HasValue first.</exception>
    public T Value => HasValue
        ? _value
        : throw new InvalidOperationException("Maybe<T> has no value. Check HasValue before accessing Value.");

    /// <summary>
    /// Returns the contained value, or the provided default if empty.
    /// </summary>
    public T GetValueOrDefault(T defaultValue) => HasValue ? _value : defaultValue;

    /// <summary>
    /// Transforms the contained value using the given projection. Returns None if empty.
    /// </summary>
    public Maybe<TResult> Map<TResult>(Func<T, TResult> map) =>
        HasValue ? Maybe<TResult>.Some(map(_value)) : Maybe<TResult>.None();

    /// <summary>
    /// Chains a Maybe-returning function. Returns None if this is empty.
    /// </summary>
    public Maybe<TResult> Bind<TResult>(Func<T, Maybe<TResult>> bind) =>
        HasValue ? bind(_value) : Maybe<TResult>.None();
}
