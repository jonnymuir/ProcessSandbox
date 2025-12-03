using System;
using System.Threading;

namespace ProcessSandbox.Tests.TestImplementations;

/// <summary>
/// Simple test service interface.
/// </summary>
public interface ITestService
{
    /// <summary>
    /// Echoes the input string.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    string Echo(string input);
    /// <summary>
    /// Adds two integers.
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    int Add(int a, int b);
    /// <summary>
    /// Does nothing.
    /// </summary>
    void DoNothing();
    /// <summary>
    /// Processes a byte array.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    byte[] ProcessBytes(byte[] data);
}

/// <summary>
/// Basic implementation for testing.
/// </summary>
public class TestServiceImpl : ITestService
{
    /// <summary>
    /// Echoes the input string.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public string Echo(string input)
    {
        return input;
    }

    /// <summary>
    /// Adds two integers.
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public int Add(int a, int b)
    {
        return a + b;
    }

    /// <summary>
    /// Does nothing.
    /// </summary>
    public void DoNothing()
    {
        // Does nothing
    }

    /// <summary>
    /// Processes a byte array by incrementing each byte by 1.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public byte[] ProcessBytes(byte[] data)
    {
        if (data == null)
            return Array.Empty<byte>();

        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] + 1);
        }
        return result;
    }
}

/// <summary>
/// Implementation that throws exceptions for testing error handling.
/// </summary>
public class ThrowingServiceImpl : ITestService
{
    /// <summary>
    /// Echoes the input string but always throws an exception.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public string Echo(string input)
    {
        throw new InvalidOperationException($"Echo failed for: {input}");
    }

    /// <summary>
    /// Adds two integers but always throws an exception.
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    /// <exception cref="DivideByZeroException"></exception>
    public int Add(int a, int b)
    {
        throw new DivideByZeroException("Cannot add numbers");
    }

    /// <summary>
    /// Does nothing but always throws an exception.
    /// </summary>
    /// <exception cref="NotImplementedException"></exception>
    public void DoNothing()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Processes a byte array but always throws an exception.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public byte[] ProcessBytes(byte[] data)
    {
        throw new ArgumentException("Invalid bytes");
    }
}

/// <summary>
/// Implementation that simulates slow operations.
/// </summary>
public class SlowServiceImpl : ITestService
{
    private readonly int _delayMs;

    /// <summary>
    /// Creates a new slow service with default delay of 1000ms.
    /// </summary>
    public SlowServiceImpl() : this(1000)
    {
    }

    /// <summary>
    /// Creates a new slow service with specified delay.
    /// </summary>
    /// <param name="delayMs"></param>
    public SlowServiceImpl(int delayMs)
    {
        _delayMs = delayMs;
    }

    /// <summary>
    /// Echoes the input string after a delay.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public string Echo(string input)
    {
        Thread.Sleep(_delayMs);
        return input;
    }

    /// <summary>
    /// Adds two integers after a delay.
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public int Add(int a, int b)
    {
        Thread.Sleep(_delayMs);
        return a + b;
    }

    /// <summary>
    /// Does nothing after a delay.
    /// </summary>
    public void DoNothing()
    {
        Thread.Sleep(_delayMs);
    }

    /// <summary>
    /// Processes a byte array after a delay.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public byte[] ProcessBytes(byte[] data)
    {
        Thread.Sleep(_delayMs);
        return data;
    }
}

/// <summary>
/// Implementation that tracks call counts.
/// </summary>
public class StatefulServiceImpl : ITestService
{
    private int _callCount;

    /// <summary>
    /// Gets the number of method calls made to this service.
    /// </summary>
    public int CallCount => _callCount;

    /// <summary>
    /// Echoes the input string and increments call count.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public string Echo(string input)
    {
        Interlocked.Increment(ref _callCount);
        return $"{input} (call #{_callCount})";
    }

    /// <summary>
    /// Adds two integers and increments call count.    
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public int Add(int a, int b)
    {
        Interlocked.Increment(ref _callCount);
        return a + b;
    }

    /// <summary>
    /// Does nothing and increments call count.
    /// </summary>
    public void DoNothing()
    {
        Interlocked.Increment(ref _callCount);
    }

    /// <summary>
    /// Processes a byte array and increments call count.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public byte[] ProcessBytes(byte[] data)
    {
        Interlocked.Increment(ref _callCount);
        return data;
    }
}