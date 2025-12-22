using System;
using System.Runtime.InteropServices;
using Contracts;

namespace LegacyLibrary;


/// <summary>
/// A legacy service that uses the COM Calculator internally
/// </summary>
public class LegacyService : ICalculator
{

    private static ICalculator GetComObject()
    {
        // Get the path to the manifest sitting next to the Worker EXE
        string manifestPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProcessSandbox.Worker.exe.manifest");

        // Manually push the manifest into the Windows activation stack
        using (new ActivationContext(manifestPath))
        {
            Type comType = Type.GetTypeFromCLSID(Guid.Parse("11111111-2222-3333-4444-555555555555"))
                ?? throw new Exception("Native COM Class not found in Context!");

            object comObject = Activator.CreateInstance(comType);
            return (ICalculator)comObject;
        }
    }

    /// <summary>
    /// Adds two integers using the COM Calculator
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public int Add(int a, int b)
    {
        var calc = GetComObject();
        return calc.Add(a, b);
    }

    /// <summary>
    /// Gets system information
    /// </summary>
    /// <returns></returns>
    public string GetInfo()
    {
        var calc = GetComObject();
        return calc.GetInfo();
    }
}
