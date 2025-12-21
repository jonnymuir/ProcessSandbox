using System;
using System.Runtime.InteropServices;
using Contracts;

namespace LegacyLibrary;

/// <summary>
/// The COM-visible Calculator class
/// </summary>
[ComVisible(true)]
[Guid("11111111-2222-3333-4444-555555555555")]
[ProgId("Legacy.Calculator")]
public class Calculator
{
    /// <summary>
    /// Adds two integers
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public int Add(int a, int b) => a + b;
}

/// <summary>
/// A legacy service that uses the COM Calculator internally
/// </summary>
public class LegacyService : ICalculator
{
    /// <summary>
    /// Adds two integers using the COM Calculator
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public int Add(int a, int b)
    {
        // Simple instantiation inside the 32-bit process
        var com = new Calculator();
        return com.Add(a, b);
    }

    /// <summary>
    /// Gets system information
    /// </summary>
    /// <returns></returns>
    public string GetSystemInfo()
    {
        return $"OS: {Environment.OSVersion} | 64Bit: {Environment.Is64BitProcess} | Ver: {Environment.Version}";
    }
}
