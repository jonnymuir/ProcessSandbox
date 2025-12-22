using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ProcessSandbox.Worker;

/// <summary>
/// Loads Native COM DLLs directly without registration
/// </summary>
public static class NativeComLoader
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    // Use IntPtr for the output to avoid auto-marshalling "Variant" errors
    private delegate int DllGetClassObjectDelegate(
        [In] ref Guid clsid,
        [In] ref Guid iid,
        out IntPtr ppv);

    [ComImport]
    [Guid("00000001-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        // Use PreserveSig to match the Delphi stdcall HRESULT
        [PreserveSig]
        int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppv);
        [PreserveSig]
        int LockServer(bool fLock);
    }

    /// <summary>
    /// Creates an instance of a native COM object from a DLL without registration
    /// </summary>
    /// <param name="dllPath"></param>
    /// <param name="clsid"></param>
    /// <param name="interfaceType"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="COMException"></exception>
    public static object CreateInstance(string dllPath, Guid clsid, Type interfaceType)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException(
                "Direct COM loading is only supported on Windows. " +
                "Check your configuration or deployment environment.");
        }

        IntPtr hModule = LoadLibrary(dllPath);
        if (hModule == IntPtr.Zero)
            throw new Exception($"Native DLL failed to load: {dllPath}");

        IntPtr pAddress = GetProcAddress(hModule, "DllGetClassObject");
        if (pAddress == IntPtr.Zero)
            throw new Exception("DllGetClassObject not found in DLL.");

        var dllGetClassObject = Marshal.GetDelegateForFunctionPointer<DllGetClassObjectDelegate>(pAddress);

        Guid iidIClassFactory = typeof(IClassFactory).GUID;

        // 1. Get the factory as a raw pointer first
        int hr = dllGetClassObject(ref clsid, ref iidIClassFactory, out IntPtr pFactory);
        if (hr < 0) throw new COMException("DllGetClassObject failed", hr);

        try
        {
            // 2. Wrap the pointer in our IClassFactory interface
            var factory = (IClassFactory)Marshal.GetObjectForIUnknown(pFactory);

            // 3. Create the actual object instance
            Guid iidInterface = interfaceType.GUID;
            hr = factory.CreateInstance(IntPtr.Zero, ref iidInterface, out IntPtr pInstance);

            if (hr < 0) throw new COMException("IClassFactory.CreateInstance failed", hr);

            // 4. Convert the final pointer to a .NET object cast to the interface
            return Marshal.GetObjectForIUnknown(pInstance);
        }
        finally
        {
            // Clean up the factory pointer
            if (pFactory != IntPtr.Zero) Marshal.Release(pFactory);
        }
    }
}