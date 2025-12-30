using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ProcessSandbox.Tests.TestImplementations;

/// <summary>
/// Internal COM object used by the Secondary COM object
/// </summary>
[ComVisible(true)]
[Guid("22222222-2222-2222-2222-222222222222")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInternalEngine
{
    /// <summary>
    /// Gets the status of the internal engine
    /// </summary>
    /// <returns></returns>
    string GetStatus();
}

/// <summary>
/// Implementation of the Internal COM object
/// </summary>
[ComVisible(true)]
[Guid("33333333-3333-3333-3333-333333333333")]
public class InternalEngine : IInternalEngine
{
    /// <summary>
    /// Gets the status of the internal engine
    /// </summary>
    /// <returns></returns>
    public string GetStatus() => "C# Internal Engine Active";
}

/// <summary>
/// Primary COM object that uses the InternalEngine COM object
/// </summary>
[ComVisible(true)]
[Guid("11111111-1111-1111-1111-111111111111")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPrimaryService
{
    /// <summary>
    /// Gets a combined report from the internal engine
    /// </summary>
    /// <returns></returns>
    string GetCombinedReport();
}

/// <summary>
/// Implementation of the Primary COM object
/// </summary>
[ComVisible(true)]
[Guid("44444444-4444-4444-4444-444444444444")]
[SupportedOSPlatform("windows")]
public class PrimaryService : IPrimaryService
{
    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        out IntPtr ppv);
    
    /// <summary>
    /// Gets a combined report from the internal engine
    /// </summary>
    /// <returns></returns>
    public string GetCombinedReport()
    {
        Guid clsid = new("33333333-3333-3333-3333-333333333333");
        Guid iid = new("22222222-2222-2222-2222-222222222222");

        // Test 1: Native call
        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out IntPtr pUnk);
        if (hr != 0) return $"Native registry check failed: {hr:X}";

        // Test 2: Managed call
        var engineType = Type.GetTypeFromCLSID(clsid);
        if (engineType == null) return "GetTypeFromCLSID failed";

        var engine = (IInternalEngine)Activator.CreateInstance(engineType)!;
        return $"Success: {engine.GetStatus()}";
    }
}