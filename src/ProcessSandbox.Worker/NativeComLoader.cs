using System;
using System.Runtime.InteropServices;

namespace ProcessSandbox.Worker;

/// <summary>
/// Loads Native COM DLLs directly without registration
/// </summary>
public static class NativeComLoader
{
    // 1. Native API Imports
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    // 2. COM Definitions
    private delegate int DllGetClassObjectDelegate(
        [In] ref Guid clsid,
        [In] ref Guid iid,
        [Out, MarshalAs(UnmanagedType.Interface)] out object ppv);

    [ComImport]
    [Guid("00000001-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        void CreateInstance([MarshalAs(UnmanagedType.IUnknown)] object pUnkOuter, ref Guid riid, out object ppv);
        void LockServer(bool fLock);
    }


    /// <summary>
    /// Creates an instance of a COM object from a native DLL without registration
    /// </summary>
    /// <param name="dllPath"></param>
    /// <param name="clsid"></param>
    /// <param name="interfaceType"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="COMException"></exception>
    public static object CreateInstance(string dllPath, Guid clsid, Type interfaceType)
    {
        // A. Load the DLL into this process
        IntPtr hModule = LoadLibrary(dllPath);
        if (hModule == IntPtr.Zero)
            throw new Exception($"Native DLL not found or failed to load: {dllPath} (Error: {Marshal.GetLastWin32Error()})");

        // B. Find the Entry Point
        IntPtr pAddress = GetProcAddress(hModule, "DllGetClassObject");
        if (pAddress == IntPtr.Zero)
            throw new Exception("DLL does not export DllGetClassObject. Is it a valid COM server?");

        // C. Convert function pointer to delegate
        var dllGetClassObject = Marshal.GetDelegateForFunctionPointer<DllGetClassObjectDelegate>(pAddress);

        // D. Call DllGetClassObject to get the Class Factory
        Guid iidIClassFactory = typeof(IClassFactory).GUID;
        int hr = dllGetClassObject(ref clsid, ref iidIClassFactory, out object factoryObj);
        
        if (hr < 0 || factoryObj == null)
            throw new COMException($"Failed to get IClassFactory for CLSID {clsid}", hr);

        // E. Ask the Factory to create the object
        var factory = (IClassFactory)factoryObj;
        Guid iidInterface = interfaceType.GUID; // This relies on the [Guid] attribute being present on the interface!
        
        factory.CreateInstance(null!, ref iidInterface, out object instance);
        
        return instance; // This is now a raw RCW cast to the specific interface
    }
}