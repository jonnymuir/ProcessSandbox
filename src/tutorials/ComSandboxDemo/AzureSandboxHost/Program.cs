using System.ComponentModel;
using Contracts;
using ProcessSandbox.Pool;
using ProcessSandbox.Proxy;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// 1. Configure the Sandbox
var config = new ProcessPoolConfiguration
{
    // Ensure we use the 32-bit .NET 4.8 Worker
    DotNetVersion = DotNetVersion.Net48_32Bit, 
    MinPoolSize = 1,
    MaxPoolSize = 2,
    
    // The com object needs putting in the win-x86 folder manually 
    ImplementationAssemblyPath = Path.Combine(
        AppContext.BaseDirectory, 
        "workers", "net48", "win-x86", "SimpleCom.dll"),

    ImplementationTypeName = "Contracts.ICalculator",
    ComClsid = new Guid("11111111-2222-3333-4444-555555555555")      
};

var loggerFactory = LoggerFactory.Create(b => 
{
    b.AddConsole();
    b.SetMinimumLevel(LogLevel.Debug);
});

// 2. Create the Proxy Factory
var proxy = await ProcessProxy.CreateAsync<ICalculator>(config, loggerFactory);

app.MapGet("/", () => 
{
    var html = @"
    <!DOCTYPE html>
    <html>
    <head>
        <title>Native COM Calculator</title>
        <style>
            body { font-family: sans-serif; display: flex; justify-content: center; padding: 50px; background: #f0f2f5; }
            .card { background: white; padding: 2rem; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1); width: 320px; }
            input { width: 100%; padding: 10px; margin: 10px 0; border: 1px solid #ddd; border-radius: 4px; box-sizing: border-box; }
            button { width: 100%; padding: 10px; background: #28a745; color: white; border: none; border-radius: 4px; cursor: pointer; font-weight: bold; }
            button:hover { background: #218838; }
            #result-box { margin-top: 20px; padding: 15px; border-radius: 4px; background: #e9ecef; display: none; text-align: center; }
            .result-val { font-size: 1.5rem; color: #007bff; font-weight: bold; }
            .info { font-size: 0.75rem; color: #666; margin-top: 1.5rem; border-top: 1px solid #eee; padding-top: 10px; line-height: 1.4; }
        </style>
    </head>
    <body>
        <div class='card'>
            <h2 style='margin-top:0'>Native COM Add</h2>
            <form id='calcForm'>
                <input type='number' id='x' name='x' placeholder='First Number' required />
                <input type='number' id='y' name='y' placeholder='Second Number' required />
                <button type='submit' id='btn'>Calculate in Sandbox</button>
            </form>

            <div id='result-box'>
                <div style='font-size: 0.9rem; color: #444;'>Total Sum:</div>
                <div id='sum-display' class='result-val'>0</div>
            </div>

            <div class='info'>
                <strong>Native Library Info:</strong><br/>
                <span id='engine-info'>Not run yet</span>
            </div>
        </div>

        <script>
            document.getElementById('calcForm').addEventListener('submit', async (e) => {
                e.preventDefault();
                const btn = document.getElementById('btn');
                const box = document.getElementById('result-box');
                const display = document.getElementById('sum-display');
                const engineInfo = document.getElementById('engine-info');
                
                btn.disabled = true;
                btn.innerText = 'Processing...';

                try {
                    const formData = new FormData(e.target);
                    const response = await fetch('/calculate', {
                        method: 'POST',
                        body: formData
                    });
                    
                    const data = await response.json();
                    
                    if (data.success) {
                        display.innerText = data.result;
                        engineInfo.innerText = data.engine;
                        box.style.display = 'block';
                    } else {
                        alert('Error: ' + data.detail);
                    }
                } catch (err) {
                    alert('Request failed. Check Azure logs.');
                } finally {
                    btn.disabled = false;
                    btn.innerText = 'Calculate in Sandbox';
                }
            });
        </script>
    </body>
    </html>";

    return Results.Content(html, "text/html");
});

app.MapPost("/calculate", async (HttpRequest request) => 
{
    try 
    {
        // Parse form values
        var form = await request.ReadFormAsync();

        if(form["x"].Count == 0 || form["y"].Count == 0)
        {
            throw new Exception("Both 'x' and 'y' values are required.");
        }

        int x = int.Parse(form["x"]!);
        int y = int.Parse(form["y"]!);

        // Call our 32-bit Native COM object via the Sandbox Proxy
        var sum = proxy.Add(x, y);
        var info = proxy.GetInfo();

        return Results.Ok(new { 
            Success = true, 
            Input = new { x, y }, 
            Result = sum, 
            Engine = info 
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.Run();
