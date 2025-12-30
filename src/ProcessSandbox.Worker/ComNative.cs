using System.Runtime.InteropServices;

internal static class ComNative
{
    
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("ole32.dll")]
    public static extern int CoRegisterClassObject(
        [In] ref Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll")]
    public static extern int CoRevokeClassObject(uint dwRegister);

    [DllImport("ole32.dll")]
    public static extern int CoResumeClassObjects();

    public delegate int DllGetClassObject(ref Guid clsid, ref Guid iid, out IntPtr ppv);
    public const uint CLSCTX_INPROC_SERVER = 0x1;
    public const uint REGCLS_MULTIPLEUSE = 1;
    public const uint REGCLS_SUSPENDED = 4;

    public const uint REGCLS_AGILE = 16;
}