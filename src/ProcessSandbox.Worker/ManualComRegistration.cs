using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
#if !NET48
using System.Runtime.Versioning;
#endif

namespace ProcessSandbox.Worker;


/// <summary>
/// Helper class to manually register COM DLLs (both native and managed) without requiring system-wide registration.
/// </summary>
public class ManualComRegistration : IDisposable
{
    private readonly List<uint> _registrationCookies = [];
    private readonly List<IntPtr> _loadedLibraries = [];

    /// <summary>
    /// Registers a COM DLL (native or managed) for use within the current process.
    /// </summary>
    /// <param name="dllPath"></param>
    /// <param name="clsid"></param>
    public void RegisterDll(string dllPath, Guid clsid)
    {
        // 1. Check if it's a .NET assembly (for your C# tests)
        if (ManualComRegistration.IsManagedAssembly(dllPath))
        {
            RegisterManagedType(dllPath, clsid);
        }
        else
        {
            // 2. Fallback to Native logic (for your Delphi DLLs)
            RegisterNativeDll(dllPath, clsid);
        }
    }

    private static bool IsManagedAssembly(string path)
    {
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch { return false; }
    }

    private void RegisterManagedType(string dllPath, Guid clsid)
    {
        var assembly = Assembly.LoadFrom(dllPath);
        var type = assembly.GetTypes().FirstOrDefault(t =>
            t.IsClass &&
            t.GetCustomAttribute<GuidAttribute>()?.Value.Equals(clsid.ToString(), StringComparison.OrdinalIgnoreCase) == true);

        if (type == null) throw new Exception($"CLSID {clsid} not found.");

        // Create the factory wrapper
#pragma warning disable CA1416 // Validate platform compatibility
        var factory = new SimpleManagedFactory(type);
#pragma warning restore CA1416 // Validate platform compatibility

        // Register the FACTORY, not the object instance
        int hr = ComNative.CoRegisterClassObject(
            ref clsid,
            factory,
            ComNative.CLSCTX_INPROC_SERVER,
            ComNative.REGCLS_MULTIPLEUSE | ComNative.REGCLS_SUSPENDED,
            out uint cookie);

        if (hr != 0) throw new Exception($"CoRegisterClassObject failed: {hr:X}");

        ComNative.CoResumeClassObjects();

        _registrationCookies.Add(cookie);
    }

    private void RegisterNativeDll(string dllPath, Guid clsid)
    {
        IntPtr hModule = ComNative.LoadLibrary(dllPath);
        if (hModule == IntPtr.Zero) throw new Exception($"Failed to load native DLL {dllPath}");
        _loadedLibraries.Add(hModule);

        IntPtr procAddr = ComNative.GetProcAddress(hModule, "DllGetClassObject");
        if (procAddr == IntPtr.Zero)
            throw new Exception($"DllGetClassObject not found in {dllPath}. Is it a COM DLL?");

        var getClassObject = Marshal.GetDelegateForFunctionPointer<ComNative.DllGetClassObject>(procAddr);

        Guid iidIClassFactory = new Guid("00000001-0000-0000-C000-000000000046");
        int hr = getClassObject(ref clsid, ref iidIClassFactory, out object factory);

        if (hr != 0) throw new Exception($"DllGetClassObject failed: {hr:X}");

        hr = ComNative.CoRegisterClassObject(
            ref clsid,
            factory,
            ComNative.CLSCTX_INPROC_SERVER,
            ComNative.REGCLS_MULTIPLEUSE | ComNative.REGCLS_SUSPENDED,
            out uint cookie);

        if (hr != 0) throw new Exception($"CoRegisterClassObject (Native) failed: {hr:X}");

        ComNative.CoResumeClassObjects();

        _registrationCookies.Add(cookie);
    }

    /// <summary>
    /// Unregisters all COM classes registered by this instance.
    /// </summary>
    public void Dispose()
    {
        foreach (var cookie in _registrationCookies) ComNative.CoRevokeClassObject(cookie);
    }
}

/// <summary>
/// Simple implementation of IClassFactory for managed types.
/// </summary>
[ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IClassFactory
{
    /// <summary>
    /// Creates an instance of the COM object.
    /// </summary>
    /// <param name="pUnkOuter"></param>
    /// <param name="riid"></param>
    /// <param name="ppvObject"></param>
    void CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
    /// <summary>
    /// Locks or unlocks the server in memory.
    /// </summary>
    /// <param name="fLock"></param>
    void LockServer(bool fLock);
}

/// <summary>
/// Simple managed class factory implementation
/// </summary>
#if !NET48
[SupportedOSPlatform("windows")]
#endif
public class SimpleManagedFactory : IClassFactory
{
    private readonly Type _type;
    /// <summary>
    /// Creates a new factory for the specified type.
    /// </summary>
    /// <param name="type"></param>
    public SimpleManagedFactory(Type type) => _type = type;

    /// <summary>
    /// Creates an instance of the COM object.
    /// </summary>
    /// <param name="pUnkOuter"></param>
    /// <param name="riid"></param>
    /// <param name="ppvObject"></param>
    /// <exception cref="COMException"></exception>
    public void CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        if (pUnkOuter != IntPtr.Zero) throw new COMException("Aggregation not supported", -2147221232); // CLASS_E_NOAGGREGATION

        object instance = Activator.CreateInstance(_type!)!;
        IntPtr pUnk = Marshal.GetIUnknownForObject(instance);

        try
        {
#if NETFRAMEWORK || NET48
            int hr = Marshal.QueryInterface(pUnk, ref riid, out ppvObject);
#else
            int hr = Marshal.QueryInterface(pUnk, in riid, out ppvObject);
#endif
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            Marshal.Release(pUnk);
        }
    }

    /// <summary>
    /// Locks or unlocks the server in memory.
    /// </summary>
    /// <param name="fLock"></param>
    public void LockServer(bool fLock) { }
}