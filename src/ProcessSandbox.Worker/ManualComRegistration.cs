using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

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
        // Load the assembly and find the type decorated with the matching GUID
        var assembly = Assembly.LoadFrom(dllPath);
        var type = assembly.GetTypes().FirstOrDefault(t =>
            t.IsClass && 
            !t.IsAbstract &&
            t.GetCustomAttribute<GuidAttribute>()?.Value.Equals(clsid.ToString(), StringComparison.OrdinalIgnoreCase) == true);

        if (type == null)
        {
            throw new Exception($"Could not find a managed type with CLSID {clsid} in {dllPath}");
        }
        
        // Create an instance. .NET automatically provides the ClassFactory plumbing 
        // when we pass a managed object to CoRegisterClassObject.
        object instance = Activator.CreateInstance(type)
            ?? throw new Exception($"Failed to create instance of {type.FullName}");

        int hr = ComNative.CoRegisterClassObject(
            ref clsid,
            instance,
            ComNative.CLSCTX_INPROC_SERVER,
            ComNative.REGCLS_MULTIPLEUSE,
            out uint cookie);

        if (hr != 0) throw new Exception($"CoRegisterClassObject (Managed) failed: {hr:X}");
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
            ComNative.REGCLS_MULTIPLEUSE,
            out uint cookie);

        if (hr != 0) throw new Exception($"CoRegisterClassObject (Native) failed: {hr:X}");
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