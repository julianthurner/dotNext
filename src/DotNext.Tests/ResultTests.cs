﻿namespace DotNext;

using Runtime.CompilerServices;

public sealed class ResultTests : Test
{
    [Fact]
    public static void EmptyResult()
    {
        var r = default(Result<int>);
        Null(r.Error);
        True(r.IsSuccessful);
        Equal(0, r.Value);
        Equal(default, r);
        True(r.TryGet(out _));
    }

    [Fact]
    public static void EmptyResult2()
    {
        var r = default(Result<int, EnvironmentVariableTarget>);
        Equal(default(EnvironmentVariableTarget), r.Error);
        True(r.IsSuccessful);
        Equal(0, r.ValueRef);
        Equal(default, r);
        True(r.TryGet(out _));
    }

    [Fact]
    public static void Choice()
    {
        var r1 = new Result<int>(10);
        var r2 = new Result<int>(new Exception());
        False(r2.IsSuccessful);
        True(r1.IsSuccessful);
        True(r1.TryGet(out var x));
        Equal(10, x);
        False(r2.TryGet(out x));
        Equal(0, x);
        var r = r1.Coalesce(r2);
        True(r.IsSuccessful);
        True(r.TryGet(out x));
        Equal(10, x);
        NotNull(r.OrNull());
    }

    [Fact]
    public static void RaiseError()
    {
        var r = new Result<decimal>(new ArithmeticException());
        Throws<ArithmeticException>(() => r.Value);
        NotNull(r.Error);
        Equal(20M, r.Or(20M));
        Equal(0M, r.ValueOrDefault);
        Null(r.OrNull());
    }

    [Fact]
    public static void RaiseError2()
    {
        var r = new Result<decimal, EnvironmentVariableTarget>(EnvironmentVariableTarget.Machine);
        Equal(EnvironmentVariableTarget.Machine, Throws<UndefinedResultException<EnvironmentVariableTarget>>(() => r.Value).ErrorCode);
        Equal(EnvironmentVariableTarget.Machine, r.Error);
        Equal(20M, r.Or(20M));
        Equal(0M, r.ValueOrDefault);
        Null(r.OrNull());
    }

    [Fact]
    public static void Operators()
    {
        var result = new Result<int>(10);
        if (result) { }
        else Fail("Unexpected Result state");
        False(!result);
        Equal(10, (int)result);
        Equal("10", result.ToString());
        Optional<int> opt = result;
        Equal(10, opt);
        Equal(10, result.OrInvoke(static () => 20));
        result = new Result<int>(new Exception());
        if (result) Fail("Unexpected Result state");
        True(!result);
        Equal(20, result.OrInvoke(static () => 20));
        opt = result;
        False(opt.HasValue);
    }

    [Fact]
    public static void Operators2()
    {
        var result = new Result<int, EnvironmentVariableTarget>(10);
        if (result) { }
        else Fail("Unexpected Result state");
        False(!result);
        Equal(10, (int)result);
        Equal("10", result.ToString());
        Optional<int> opt = result;
        Equal(10, opt);
        Equal(10, result.OrInvoke(static () => 20));
        result = new Result<int, EnvironmentVariableTarget>(EnvironmentVariableTarget.Machine);
        if (result) Fail("Unexpected Result state");
        True(!result);
        Equal(20, result.OrInvoke(static () => 20));
        opt = result;
        False(opt.HasValue);
    }

    [Fact]
    public static void Boxing()
    {
        Equal("Hello", new Result<string>("Hello").Box());
        Null(new Result<string>(default(string)).Box().Value);
        IsType<ArithmeticException>(new Result<int>(new ArithmeticException()).Box().Error);

        Equal("Hello", new Result<string, EnvironmentVariableTarget>("Hello").Box());
        Null(new Result<string>(default(string)).Box().Value);
    }

    [Fact]
    public static void OptionalInterop()
    {
        var result = (Result<string>)Optional<string>.None;
        False(result.IsSuccessful);
        Throws<InvalidOperationException>(() => result.Value);

        result = (Result<string>)new Optional<string>(null);
        True(result.IsSuccessful);
        Null(result.Value);

        result = (Result<string>)new Optional<string>("Hello, world!");
        True(result.IsSuccessful);
        Equal("Hello, world!", result.Value);
        Equal("Hello, world!", Optional<string>.Create(result));
    }

    [Fact]
    public static void OptionalInterop2()
    {
        Result<string, EnvironmentVariableTarget> result = "Hello, world!";
        Optional<string> opt = result;
        Equal("Hello, world!", opt);

        opt = Optional<string>.Create(result);
        Equal("Hello, world!", opt);

        result = new(EnvironmentVariableTarget.Machine);
        opt = result;
        False(opt.HasValue);
    }

    [Fact]
    public static void UnderlyingType()
    {
        var type = Result.GetUnderlyingType(typeof(Result<>));
        Null(type);

        type = Result.GetUnderlyingType(typeof(int));
        Null(type);

        type = Result.GetUnderlyingType(typeof(Result<string>));
        Equal(typeof(string), type);

        type = Result.GetUnderlyingType(typeof(Result<float, EnvironmentVariableTarget>));
        Equal(typeof(float), type);
    }

    [Fact]
    public static unsafe void Conversion()
    {
        Result<float> result = 20F;
        Equal(20, result.Convert(Convert.ToInt32));

        result = new(new Exception());
        False(result.Convert(&Convert.ToInt32).IsSuccessful);
    }

    [Fact]
    public static unsafe void Conversion2()
    {
        Result<float, EnvironmentVariableTarget> result = 20F;
        Equal(20, result.Convert(Convert.ToInt32));

        result = new(EnvironmentVariableTarget.Machine);
        Equal(EnvironmentVariableTarget.Machine, result.Convert(&Convert.ToInt32).Error);
    }

    [Fact]
    public static unsafe void ConvertToResult()
    {
        // Standard conversion
        Result<string> validStringResult = "20";
        var convertedResult1 = validStringResult.Convert(ToInt);
        True(convertedResult1.IsSuccessful);
        Equal(20, convertedResult1);

        // Unsafe standard conversion
        var convertedResult2 = validStringResult.Convert<int>(&ToInt);
        True(convertedResult2.IsSuccessful);
        Equal(20, convertedResult2);

        // Failing conversion
        Result<string> invalidStringResult = "20F";
        var convertedResult3 = invalidStringResult.Convert(ToInt);
        False(convertedResult3.IsSuccessful);
        IsType<FormatException>(convertedResult3.Error);
        
        // Unsafe failing conversion
        var convertedResult4 = invalidStringResult.Convert<int>(&ToInt);
        False(convertedResult4.IsSuccessful);
        IsType<FormatException>(convertedResult4.Error);

        // Conversion of unsuccessful Result<T>
        Result<string> exceptionResult = new(new ArgumentNullException());
        var convertedResult5 = exceptionResult.Convert(ToInt);
        False(convertedResult5.IsSuccessful);
        IsType<ArgumentNullException>(convertedResult5.Error);

        // Unsafe conversion of unsuccessful Result<T>
        var convertedResult6 = exceptionResult.Convert<int>(&ToInt);
        False(convertedResult6.IsSuccessful);
        IsType<ArgumentNullException>(convertedResult6.Error);

        static Result<int> ToInt(string value) => int.TryParse(value, out var result) ? result : throw new FormatException();
    }

    [Fact]
    public unsafe static void ConvertToResultWithErrorCode()
    {
        // Standard conversion
        Result<string, EnvironmentVariableTarget> validStringResult = "20";
        var convertedResult1 = validStringResult.Convert(ToInt);
        True(convertedResult1.IsSuccessful);
        Equal(20, convertedResult1);

        // Unsafe standard conversion
        var convertedResult2 = validStringResult.Convert<int>(&ToInt);
        True(convertedResult2.IsSuccessful);
        Equal(20, convertedResult2);

        // Failing conversion
        Result<string, EnvironmentVariableTarget> invalidStringResult = "20F";
        var convertedResult3 = invalidStringResult.Convert(ToInt);
        False(convertedResult3.IsSuccessful);
        Equal(EnvironmentVariableTarget.Machine, convertedResult3.Error);

        // Unsafe failing conversion
        var convertedResult4 = invalidStringResult.Convert<int>(&ToInt);
        False(convertedResult4.IsSuccessful);
        Equal(EnvironmentVariableTarget.Machine, convertedResult4.Error);

        // Conversion of unsuccessful Result<T>
        Result<string, EnvironmentVariableTarget> errorCodeResult = new(EnvironmentVariableTarget.User);
        var convertedResult5 = errorCodeResult.Convert(ToInt);
        False(convertedResult5.IsSuccessful);
        Equal(EnvironmentVariableTarget.User, convertedResult5.Error);

        // Unsafe conversion of unsuccessful Result<T>
        var convertedResult6 = errorCodeResult.Convert<int>(&ToInt);
        False(convertedResult6.IsSuccessful);
        Equal(EnvironmentVariableTarget.User, convertedResult6.Error);

        static Result<int, EnvironmentVariableTarget> ToInt(string value) => int.TryParse(value, out var result) ? new(result) : new(EnvironmentVariableTarget.Machine);
    }

    [Fact]
    public static void HandleException()
    {
        Result<int> result = 20;
        Equal(20, result.OrInvoke(static e => 10));

        result = new(new ArithmeticException());
        Equal(10, result.OrInvoke(static e => 10));
    }

    [Fact]
    public static void HandleException2()
    {
        Result<int, EnvironmentVariableTarget> result = 20;
        Equal(20, result.OrInvoke(static e => 10));

        result = new(EnvironmentVariableTarget.Machine);
        Equal(10, result.OrInvoke(static e => 10));
    }

    [Fact]
    public static void FromErrorFactory()
    {
        False(FromError<Exception, Result<int>>(new Exception()).IsSuccessful);
        False(FromError<EnvironmentVariableTarget, Result<int, EnvironmentVariableTarget>>(EnvironmentVariableTarget.Machine).IsSuccessful);
    }

    [Fact]
    public static void ResultToDelegate()
    {
        IFunctional<Func<object>> functional = Result.FromException<int>(new Exception());
        Null(functional.ToDelegate().Invoke());

        functional = new Result<int>(42);
        Equal(42, functional.ToDelegate().Invoke());

        functional = new Result<int, EnvironmentVariableTarget>(EnvironmentVariableTarget.Machine);
        Null(functional.ToDelegate().Invoke());

        functional = new Result<int, EnvironmentVariableTarget>();
        NotNull(functional.ToDelegate().Invoke());
    }

    private static TResult FromError<TError, TResult>(TError error)
        where TResult : struct, IResultMonad<int, TError, TResult>
        => TResult.FromError(error);

    [Fact]
    public static async Task ConvertToTask()
    {
        Result<int> result = 42;
        Equal(42, await (ValueTask<int>)result);

        result = Result.FromException<int>(new OperationCanceledException(new CancellationToken(canceled: true)));
        True(result.AsTask().IsCanceled);

        result = Result.FromException<int>(new Exception());
        await ThrowsAsync<Exception>(result.AsTask().AsTask);
    }

    [Fact]
    public static void ValueRef()
    {
        Result<int> result = 42;
        Equal(42, result.ValueRef);

        result = Result.FromException<int>(new ArithmeticException());
        Throws<ArithmeticException>(() => result.ValueRef);
    }

    [Fact]
    public static void NonNullResult()
    {
        var result = default(Result<string>).EnsureNotNull();
        False(result.IsSuccessful);
        Throws<NullReferenceException>(() => result.Value);

        result = Result.FromValue("").EnsureNotNull();
        True(result.IsSuccessful);
    }
}