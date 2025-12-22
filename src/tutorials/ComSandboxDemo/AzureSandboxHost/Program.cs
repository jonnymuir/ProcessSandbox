using System.ComponentModel;
using Contracts;
using ProcessSandbox.Pool;
using ProcessSandbox.Proxy;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var loggerFactory = LoggerFactory.Create(b =>
{
    b.AddConsole();
    b.SetMinimumLevel(LogLevel.Debug);
});

var proxyC = await ProcessProxy.CreateAsync<ICalculator>(new ProcessPoolConfiguration
{
    DotNetVersion = DotNetVersion.Net48_32Bit,
    ImplementationAssemblyPath = Path.Combine(AppContext.BaseDirectory, "workers", "net48", "win-x86", "SimpleCom.dll"),
    ComClsid = new Guid("11111111-2222-3333-4444-555555555555")
}, loggerFactory);

var proxyDelphi = await ProcessProxy.CreateAsync<ICalculator>(new ProcessPoolConfiguration
{
    DotNetVersion = DotNetVersion.Net48_32Bit,
    ImplementationAssemblyPath = Path.Combine(AppContext.BaseDirectory, "workers", "net48", "win-x86", "SimpleComDelphi.dll"),
    ComClsid = new Guid("11111111-2222-3333-4444-555555555555")
}, loggerFactory);

app.MapGet("/", () =>
{
var html = @"
    <!DOCTYPE html>
    <html>
    <head>
        <title>Native COM Sandbox</title>
        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
        <style>
            body { font-family: sans-serif; display: flex; justify-content: center; padding: 50px; background: #f0f2f5; }
            .card { 
                background: white; 
                padding: 2rem; 
                border-radius: 8px; 
                box-shadow: 0 4px 6px rgba(0,0,0,0.1); 
                width: 90%;
                max-width: 400px;
                box-sizing: border-box; 
            }            
            input, select { height: 2.5rem; width: 100%; padding: 10px; margin: 10px 0; border: 1px solid #ddd; border-radius: 4px; box-sizing: border-box; }
            label { font-size: 0.8rem; font-weight: bold; color: #555; }
            button { width: 100%; padding: 10px; background: #007bff; color: white; border: none; border-radius: 4px; cursor: pointer; font-weight: bold; margin-top: 10px;}
            button:hover { background: #0056b3; }
            #result-box { margin-top: 20px; padding: 15px; border-radius: 4px; background: #e9ecef; display: none; text-align: center; }
            .result-val { font-size: 1.5rem; color: #28a745; font-weight: bold; }
            .info { font-size: 0.75rem; color: #666; margin-top: 1.5rem; border-top: 1px solid #eee; padding-top: 10px; line-height: 1.4; }
        </style>
    </head>
    <body>
        <div class='card'>
            <h2 style='margin-top:0'>COM Sandbox Multi-Engine</h2>
            <form id='calcForm'>
                <label>Target Engine</label>
                <select name='engine' id='engine'>
                    <option value='c'>SimpleCom (C language)</option>
                    <option value='delphi'>SimpleComDelphi (Delphi)</option>
                </select>

                <label>Inputs</label>
                <input type='number' id='x' name='x' placeholder='First Number' value='10' required />
                <input type='number' id='y' name='y' placeholder='Second Number' value='5' required />
                
                <button type='submit' id='btn'>Calculate in Sandbox</button>
            </form>

            <div id='result-box'>
                <div style='font-size: 0.8rem; color: #444;'>Result:</div>
                <div id='sum-display' class='result-val'>0</div>
                <div id='engine-info' style='font-size: 0.7rem; color: #666; margin-top: 5px; font-style: italic;'></div>
            </div>
        </div>

        <script>
            document.getElementById('calcForm').addEventListener('submit', async (e) => {
                e.preventDefault();
                const btn = document.getElementById('btn');
                const box = document.getElementById('result-box');
                
                btn.disabled = true;
                btn.innerText = 'Routing to Worker...';

                try {
                    const formData = new FormData(e.target);
                    const response = await fetch('/calculate', { method: 'POST', body: formData });
                    const data = await response.json();
                    
                    if (data.success) {
                        document.getElementById('sum-display').innerText = data.result;
                        document.getElementById('engine-info').innerText = 'Backend: ' + data.engine;
                        box.style.display = 'block';
                    } else {
                        alert('Error: ' + data.detail);
                    }
                } catch (err) {
                    alert('Request failed.');
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

        if (form["x"].Count == 0 || form["y"].Count == 0)
        {
            throw new Exception("Both 'x' and 'y' values are required.");
        }

        int x = int.Parse(form["x"]!);
        int y = int.Parse(form["y"]!);
        string? engine = form["engine"];

        var activeProxy = engine == "delphi" ? proxyDelphi : proxyC;

        // Call our 32-bit Native COM object via the Sandbox Proxy
        var sum = activeProxy.Add(x, y);
        var info = activeProxy.GetInfo();

        return Results.Ok(new
        {
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
