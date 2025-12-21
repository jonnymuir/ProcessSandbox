namespace Contracts;

/// <summary>
/// A simple calculator interface
/// </summary>
public interface ICalculator
{
    /// <summary>
    /// Adds two integers
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    int Add(int a, int b);
    /// <summary>
    /// Gets system information
    /// </summary>
    /// <returns></returns>
    string GetSystemInfo();
}
