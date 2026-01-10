using System;
using System.Threading;

namespace ProcessSandbox.Tests.TestImplementations;

/// <summary>
/// Simple test service interface.
/// </summary>
public interface ITestService : IDisposable
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

    /// <summary>
    /// Set
    /// </summary>
    /// <param name="value"></param>
    void Set(string value);
    
    /// <summary>
    /// Read
    /// </summary>
    /// <returns></returns>
    string Read();
}

/// <summary>
/// Basic implementation for testing.
/// </summary>
public class TestServiceImpl : ITestService
{
    private string value = string.Empty;

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

    /// <summary>
    /// Set method 
    /// </summary>
    /// <param name="value"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void Set(string value)
    {
        this.value = value;
    }

    /// <summary>
    /// Read method
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public string Read()
    {
        return value;
    }

    /// <summary>
    /// Disposes the service.
    /// </summary>
    public void Dispose()
    {
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

    /// <summary>
    /// Set method that always throws an exception.
    /// </summary>
    /// <param name="value"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void Set(string value)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Read method that always throws an exception.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public string Read()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Disposes the service.
    /// </summary>
    public void Dispose()
    {
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

    /// <summary>
    /// Set method that always throws an exception.
    /// </summary>
    /// <param name="value"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void Set(string value)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Read method that always throws an exception.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public string Read()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Disposes the service.
    /// </summary>
    public void Dispose()
    {
    }


}

/// <summary>
/// Implementation that tracks call counts.
/// </summary>
public class StatefulServiceImpl : ITestService
{
    private static int _callCount;

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

    /// <summary>
    /// Set method that always throws an exception.
    /// </summary>
    /// <param name="value"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void Set(string value)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Read method that always throws an exception.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public string Read()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Disposes the service.
    /// </summary>
    public void Dispose()
    {
    }

}

/// <summary>
/// Implementation that tracks call counts.
/// </summary>
public class LeakyServiceImpl : ITestService
{
    // A static list that never gets cleared = Classic Memory Leak
    private static readonly List<byte[]> _memoryHog = new();

    /// <summary>
    /// Echoes the input string
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public string Echo(string input)
    {
        if (input == "crash")
        {
            // Environment.FailFast bypasses all try/catch/finally blocks 
            // and terminates the process immediately.
            Environment.FailFast("Simulating a native crash for sandbox testing.");
        }

        // Parse input - can be in format "text" or "text:megabytes"
        var parts = input.Split(':', 2);
        if (parts.Length == 2 && parts[0] == "memoryleak" && int.TryParse(parts[1], out var mb) && mb > 0)
        {
            // Allocate unmanaged memory to simulate a heavy leak
            var data = new byte[mb * 1024 * 1024];

            // Fill it so it actually commits to RAM
            new Random().NextBytes(data);

            // Add to static list so GC cannot collect it
            _memoryHog.Add(data);
        }

        return $"{input}";
    }

    /// <summary>
    /// Adds two integers
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public int Add(int a, int b)
    {
        return a + b;
    }

    /// <summary>
    /// Does nothing 
    /// </summary>
    public void DoNothing()
    {
    }

    /// <summary>
    /// Processes a byte array
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public byte[] ProcessBytes(byte[] data)
    {
        return data;
    }

    /// <summary>
    /// Set method that always throws an exception.
    /// </summary>
    /// <param name="value"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void Set(string value)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Read method that always throws an exception.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public string Read()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Disposes the service.
    /// </summary>
    public void Dispose()
    {
    }

}