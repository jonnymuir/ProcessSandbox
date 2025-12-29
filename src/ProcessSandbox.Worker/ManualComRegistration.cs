using System.Runtime.InteropServices;


/// <summary>
/// Handles manual COM registration for in-process COM servers.
/// </summary>
public class ManualComRegistration : IDisposable
{
    private readonly List<uint> _registrationCookies = new();
    private readonly List<IntPtr> _loadedLibraries = new();

    /// <summary>
    /// Registers a COM DLL manually in the current process.
    /// </summary>
    /// <param name="dllPath"></param>
    /// <param name="clsid"></param>
    /// <exception cref="Exception"></exception>
    public void RegisterDll(string dllPath, Guid clsid)
    {
        IntPtr hModule = ComNative.LoadLibrary(dllPath);
        if (hModule == IntPtr.Zero) throw new Exception($"Failed to load {dllPath}");
        _loadedLibraries.Add(hModule);

        IntPtr procAddr = ComNative.GetProcAddress(hModule, "DllGetClassObject");
        if (procAddr == IntPtr.Zero) throw new Exception("DllGetClassObject not found in DLL");

        var getClassObject = Marshal.GetDelegateForFunctionPointer<ComNative.DllGetClassObject>(procAddr);

        // Ask the DLL for its Factory (IClassFactory)
        Guid iidIClassFactory = new Guid("00000001-0000-0000-C000-000000000046");
        int hr = getClassObject(ref clsid, ref iidIClassFactory, out object factory);

        if (hr != 0) throw new Exception($"DllGetClassObject failed with HR: {hr:X}");

        // Register the factory in the process's internal COM table
        hr = ComNative.CoRegisterClassObject(
            ref clsid, 
            factory, 
            ComNative.CLSCTX_INPROC_SERVER, 
            ComNative.REGCLS_MULTIPLEUSE, 
            out uint cookie);

        if (hr != 0) throw new Exception($"CoRegisterClassObject failed with HR: {hr:X}");
        _registrationCookies.Add(cookie);
    }

    /// <summary>
    /// Unregisters all COM classes and frees loaded DLLs.
    /// </summary>
    public void Dispose()
    {
        foreach (var cookie in _registrationCookies) ComNative.CoRevokeClassObject(cookie);
        // We typically don't FreeLibrary COM DLLs until process exit to avoid crashes
    }
}