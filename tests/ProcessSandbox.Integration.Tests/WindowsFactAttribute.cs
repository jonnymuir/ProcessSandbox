using System.Runtime.InteropServices;
using Xunit;

namespace ProcessSandbox.Integration.Tests;



/// <summary>
/// Fact attribute that skips tests on non-Windows platforms.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsFactAttribute"/> class.
    /// </summary>
    public WindowsFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Skip = "This test requires the Windows COM subsystem and only runs on Windows.";
        }
    }
}