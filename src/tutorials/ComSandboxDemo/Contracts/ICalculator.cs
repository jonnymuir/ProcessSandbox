using System.Runtime.InteropServices;
namespace Contracts;

/// <summary>
/// A simple calculator interface
/// </summary>
[ComImport]
[Guid("E1234567-ABCD-1234-EF12-0123456789AB")] // Matches the IID in our C code
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ICalculator : IDisposable
{
    /// <summary>
    /// Adds two integers
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    [PreserveSig] // Native COM usually returns HRESULT; this ensures 'int' is treated as the direct return
    int Add(int a, int b);
    /// <summary>
    /// Gets system information
    /// </summary>
    /// <returns></returns>
    [PreserveSig]
    [return: MarshalAs(UnmanagedType.BStr)] // Explicitly tell .NET to expect a BSTR
    string GetInfo();
}
