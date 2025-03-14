﻿using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace DotNext.Threading.Tasks;

using System.Threading.Tasks;
using Runtime.CompilerServices;

/// <summary>
/// Provides task result conversion methods.
/// </summary>
public static class Conversion
{
    /// <summary>
    /// Converts one type of task into another.
    /// </summary>
    /// <typeparam name="TInput">The source task type.</typeparam>
    /// <typeparam name="TOutput">The target task type.</typeparam>
    /// <param name="task">The task to convert.</param>
    /// <returns>The converted task.</returns>
    public static Task<TOutput> Convert<TInput, TOutput>(this Task<TInput> task)
        where TInput : TOutput => task.Convert(Converter.Identity<TInput, TOutput>());

    /// <summary>
    /// Converts one type of <see cref="AwaitableResult{T}"/> into another.
    /// </summary>
    /// <typeparam name="TInput">The source Result type.</typeparam>
    /// <typeparam name="TOutput">The target Result type.</typeparam>
    /// <param name="awaitableResult">The awaitable Result to convert.</param>
    /// <returns>The converted task.</returns>
    public static AwaitableResult<TOutput> Convert<TInput, TOutput>(this AwaitableResult<TInput> awaitableResult)
        where TInput : TOutput => awaitableResult.Convert(Converter.Identity<TInput, TOutput>());

    /// <summary>
    /// Converts one type of task into another.
    /// </summary>
    /// <typeparam name="TInput">The source task type.</typeparam>
    /// <typeparam name="TOutput">The target task type.</typeparam>
    /// <param name="task">The task to convert.</param>
    /// <param name="converter">Non-blocking conversion function.</param>
    /// <returns>The converted task.</returns>
    public static async Task<TOutput> Convert<TInput, TOutput>(this Task<TInput> task, Converter<TInput, TOutput> converter)
        => converter(await task.ConfigureAwait(false));

    /// <summary>
    /// Converts one type of <see cref="AwaitableResult{T}"/> into another.
    /// </summary>
    /// <typeparam name="TInput">The source Result type.</typeparam>
    /// <typeparam name="TOutput">The target Result type.</typeparam>
    /// <param name="awaitableResult">The awaitable Result to convert.</param>
    /// <param name="converter">Non-blocking conversion function.</param>
    /// <returns>The converted task.</returns>
    public static AwaitableResult<TOutput> Convert<TInput, TOutput>(this AwaitableResult<TInput> awaitableResult, Converter<TInput, TOutput> converter)
    {
        async Task<TOutput> ConvertInternal()
        {
            var result = await awaitableResult.ConfigureAwait(false);
            return result.IsSuccessful ? converter(result.Value) : throw result.Error;
        }

        return ConvertInternal().SuspendException();
    }

    /// <summary>
    /// Converts value type into nullable value type.
    /// </summary>
    /// <param name="task">The task to convert.</param>
    /// <typeparam name="T">The value type.</typeparam>
    /// <returns>The converted task.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task<T?> ToNullable<T>(this Task<T> task)
        where T : struct
        => Convert(task, static value => new T?(value));

    /// <summary>
    /// Converts one type of task into another.
    /// </summary>
    /// <typeparam name="TInput">The source task type.</typeparam>
    /// <typeparam name="TOutput">The target task type.</typeparam>
    /// <param name="task">The task to convert.</param>
    /// <param name="converter">Asynchronous conversion function.</param>
    /// <returns>The converted task.</returns>
    public static async Task<TOutput> Convert<TInput, TOutput>(this Task<TInput> task, Converter<TInput, Task<TOutput>> converter)
        => await converter(await task.ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>
    /// Converts one type of <see cref="AwaitableResult{T}"/> into another.
    /// </summary>
    /// <typeparam name="TInput">The source Result type.</typeparam>
    /// <typeparam name="TOutput">The target Result type.</typeparam>
    /// <param name="awaitableResult">The awaitable Result to convert.</param>
    /// <param name="converter">Asynchronous conversion function.</param>
    /// <returns>The converted task.</returns>
    public static AwaitableResult<TOutput> Convert<TInput, TOutput>(this AwaitableResult<TInput> awaitableResult, Converter<TInput, AwaitableResult<TOutput>> converter)
    {
        async Task<TOutput> ConvertInternal()
        {
            var result = await awaitableResult.ConfigureAwait(false);
            var conversionResult = result.IsSuccessful ? await converter(result.Value).ConfigureAwait(false) : throw result.Error;
            return conversionResult.IsSuccessful ? conversionResult.Value : throw conversionResult.Error;
        }

        return ConvertInternal().SuspendException();
    }

    /// <summary>
    /// Allows to convert <see cref="Task{TResult}"/> of unknown result type into dynamically
    /// typed task which result can be obtained as <see cref="object"/>
    /// or any other data type using <c>dynamic</c> approach.
    /// </summary>
    /// <remarks>
    /// The type of the returned task is not known at compile time and therefore treated as <c>dynamic</c>. The result value returned
    /// by <c>await</c> operator is equal to <see cref="System.Reflection.Missing.Value"/> if <paramref name="task"/> is not of type <see cref="Task{TResult}"/>.
    /// </remarks>
    /// <param name="task">The arbitrary task of type <see cref="Task{TResult}"/>.</param>
    /// <returns>The dynamically typed task.</returns>
    [RequiresUnreferencedCode("Runtime binding may be incompatible with IL trimming")]
    public static DynamicTaskAwaitable AsDynamic(this Task task) => new(task);

    /// <summary>
    /// Returns a task that never throws an exception.
    /// </summary>
    /// <param name="task">The task to convert.</param>
    /// <typeparam name="T">The type of the task.</typeparam>
    /// <returns>The task that never throws an exception. Instead, the <see cref="Result{T}"/> contains an exception.</returns>
    public static AwaitableResult<T> SuspendException<T>(this Task<T> task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return new(task);
    }

    /// <summary>
    /// Suspends the exception that can be raised by the task.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="filter">The filter of the exception to be suspended.</param>
    /// <returns>The awaitable object that suspends exceptions according to the filter.</returns>
    public static SuspendedExceptionTaskAwaitable SuspendException(this Task task, Predicate<Exception>? filter = null)
        => new(task) { Filter = filter };

    /// <summary>
    /// Suspends the exception that can be raised by the task.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="filter">The filter of the exception to be suspended.</param>
    /// <returns>The awaitable object that suspends exceptions according to the filter.</returns>
    public static SuspendedExceptionTaskAwaitable SuspendException(this ValueTask task, Predicate<Exception>? filter = null)
        => new(task) { Filter = filter };

    /// <summary>
    /// Suspends the exception that can be raised by the task.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="arg">The argument to be passed to the filter.</param>
    /// <param name="filter">The filter of the exception to be suspended.</param>
    /// <returns>The awaitable object that suspends exceptions according to the filter.</returns>
    public static SuspendedExceptionTaskAwaitable<TArg> SuspendException<TArg>(this Task task, TArg arg, Func<Exception, TArg, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        
        return new(task, arg, filter);
    }

    /// <summary>
    /// Suspends the exception that can be raised by the task.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="arg">The argument to be passed to the filter.</param>
    /// <param name="filter">The filter of the exception to be suspended.</param>
    /// <returns>The awaitable object that suspends exceptions according to the filter.</returns>
    public static SuspendedExceptionTaskAwaitable<TArg> SuspendException<TArg>(this ValueTask task, TArg arg, Func<Exception, TArg, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        
        return new(task, arg, filter);
    }
}