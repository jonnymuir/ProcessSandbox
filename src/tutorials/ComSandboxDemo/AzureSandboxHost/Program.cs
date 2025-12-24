using Contracts;
using ProcessSandbox.Pool;
using ProcessSandbox.Proxy;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var loggerFactory = LoggerFactory.Create(b =>
{
    b.AddConsole();
    b.SetMinimumLevel(LogLevel.Debug);
});

// Setup C Engine Configuration (.NET 4.8 compatible)
var poolConfigC = new ProcessPoolConfiguration
{
    DotNetVersion = DotNetVersion.Net48_32Bit,
    ComClsid = new Guid("11111111-2222-3333-4444-555555555555"),
    MaxMemoryMB = 1024,
    ProcessRecycleThreshold = 20, 
    ImplementationAssemblyPath = Path.Combine(AppContext.BaseDirectory, "workers", "net48", "win-x86", "SimpleCom.dll")
};

// Setup Delphi Engine Configuration (.NET 4.8 compatible)
var poolConfigDelphi = new ProcessPoolConfiguration
{
    DotNetVersion = DotNetVersion.Net48_32Bit,
    ComClsid = new Guid("11111111-2222-3333-4444-555555555555"),
    MaxMemoryMB = 1024,
    ProcessRecycleThreshold = 20,
    ImplementationAssemblyPath = Path.Combine(AppContext.BaseDirectory, "workers", "net48", "win-x86", "SimpleComDelphi.dll")
};

var proxyC = await ProcessProxy.CreateAsync<ICalculator>(poolConfigC, loggerFactory);
var proxyDelphi = await ProcessProxy.CreateAsync<ICalculator>(poolConfigDelphi, loggerFactory);

app.MapGet("/", () =>
{
    var html = @"
    <!DOCTYPE html>
    <html>
    <head>
        <title>COM Sandbox Concurrency Dashboard</title>
        <style>
            body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background: #f0f2f5; margin: 0; padding: 20px; color: #333; }
            .container { max-width: 1100px; margin: 0 auto; display: grid; grid-template-columns: 350px 1fr; gap: 20px; }
            .card { background: white; padding: 1.5rem; border-radius: 12px; box-shadow: 0 4px 12px rgba(0,0,0,0.08); height: fit-content; }
            h2 { margin-top: 0; font-size: 1.2rem; border-bottom: 2px solid #eee; padding-bottom: 10px; }
            
            label { display: block; font-size: 0.8rem; font-weight: bold; margin-top: 15px; color: #666; }
            input, select { width: 100%; padding: 10px; margin: 5px 0 10px; border: 1px solid #ddd; border-radius: 6px; box-sizing: border-box; }
            
            .btn-group { display: flex; gap: 10px; margin-top: 10px; }
            button { flex: 1; padding: 12px; border: none; border-radius: 6px; cursor: pointer; font-weight: bold; transition: all 0.2s; }
            #btn-start { background: #007bff; color: white; }
            #btn-start:disabled { background: #ccc; cursor: not-allowed; }
            #btn-cancel { background: #dc3545; color: white; display: none; }
            
            .status-bar { background: #333; color: white; padding: 15px; border-radius: 8px; margin-bottom: 20px; display: flex; justify-content: space-between; font-family: monospace; font-size: 0.9rem; }
            
            .process-group { margin-bottom: 15px; border: 1px solid #e0e0e0; border-radius: 8px; overflow: hidden; background: white; transition: all 0.3s ease; }
            .process-header { background: #f8f9fa; padding: 10px 15px; display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid #eee; }
            .pid-tag { background: #007bff; color: white; padding: 2px 8px; border-radius: 4px; font-size: 0.8rem; }
            .current-tag { background: #28a745; box-shadow: 0 0 8px rgba(40,167,69,0.4); }
            
            table { width: 100%; border-collapse: collapse; font-size: 0.85rem; }
            th { text-align: left; padding: 8px 15px; color: #888; border-bottom: 1px solid #eee; }
            td { padding: 8px 15px; border-bottom: 1px solid #f9f9f9; }
            .result-cell { font-size: 1.4rem; font-weight: bold; color: #28a745; background: #f0f9ff; text-align: center; width: 120px; }

            #error-box { margin-top: 20px; display: none; }
            .error-item { background: #fff1f0; border: 1px solid #ffa39e; color: #cf1322; padding: 8px 12px; border-radius: 4px; margin-bottom: 5px; font-size: 0.85rem; font-family: monospace; }
        </style>
    </head>
    <body>
        <div class='container'>
            <div class='card'>
                <h2>Configuration</h2>
                <form id='calcForm'>
                    <label>Engine</label>
                    <select id='engine'>
                        <option value='c'>C (SimpleCom.dll)</option>
                        <option value='delphi'>Delphi (SimpleComDelphi.dll)</option>
                    </select>

                    <label>Threads (Concurrency)</label>
                    <input type='number' id='threads' value='5' min='1' max='20' />

                    <label>Total Iterations</label>
                    <input type='number' id='iters' value='100' min='1' />

                    <label>Fixed Values (X + Y)</label>
                    <div style='display:flex; gap:10px'>
                        <input type='number' id='x' value='10' />
                        <input type='number' id='y' value='5' />
                    </div>
                    
                    <div class='btn-group'>
                        <button type='submit' id='btn-start'>Start Run</button>
                        <button type='button' id='btn-cancel'>Cancel</button>
                    </div>
                </form>
            </div>

            <div class='dashboard'>
                <div class='status-bar'>
                    <div>COMPLETED: <span id='stat-iter'>0/0</span></div>
                    <div>ELAPSED: <span id='stat-time'>0.0s</span></div>
                    <div>ENGINE: <span id='stat-engine'>-</span></div>
                </div>
                <div id='error-box'>
                    <h3 style='color: #cf1322; font-size: 1rem;'>Errors Encountered</h3>
                    <div id='error-list'></div>
                </div>
                <div id='process-list'></div>
            </div>
        </div>

        <script>
            let abortController = null;
            let processStats = {}; 
            let globalErrors = [];
            let startTime = null;
            let completedIters = 0;
            let targetIters = 0;

            function formatBytes(bytes) {
                if (bytes === 0) return '0 B';
                const mb = bytes / (1024 * 1024);
                return mb.toFixed(2) + ' MB';
            }

            function updateDisplay() {
                const container = document.getElementById('process-list');
                const errorBox = document.getElementById('error-box');
                const errorList = document.getElementById('error-list');
                
                // Update PID Cards
                const sortedPids = Object.keys(processStats).sort((a, b) => {
                    return processStats[b].firstSeen - processStats[a].firstSeen;
                });

                container.innerHTML = sortedPids.map(pid => {
                    const s = processStats[pid];
                    const isActive = (new Date() - s.lastTime) < 800;
                    return `
                        <div class='process-group'>
                            <div class='process-header'>
                                <span><span class='pid-tag ${isActive ? 'current-tag' : ''}'>PID ${pid}</span> <strong>${s.engine}</strong></span>
                                <span style='font-size:0.8rem; color:#666'>Last update: ${s.lastTime.toLocaleTimeString()}</span>
                            </div>
                            <table>
                                <tr>
                                    <th>Metric</th><th>Min</th><th>Max</th><th>Last</th>
                                    <th style='text-align:center'>Count</th>
                                    <th style='text-align:center; background:#f0f9ff'>Last Result</th>
                                </tr>
                                <tr>
                                    <td>Memory</td>
                                    <td>${formatBytes(s.memMin)}</td><td>${formatBytes(s.memMax)}</td><td>${formatBytes(s.memLast)}</td>
                                    <td rowspan='2' style='vertical-align:middle; text-align:center; font-size:1.2rem; font-weight:bold; border-left:1px solid #eee'>${s.count}</td>
                                    <td rowspan='2' class='result-cell'>${s.lastResult}</td>
                                </tr>
                                <tr>
                                    <td>Handles</td>
                                    <td>${s.hndMin}</td><td>${s.hndMax}</td><td>${s.hndLast}</td>
                                </tr>
                            </table>
                        </div>
                    `;
                }).join('');

                // Update Errors
                if (globalErrors.length > 0) {
                    errorBox.style.display = 'block';
                    errorList.innerHTML = globalErrors.map(err => `<div class='error-item'>[${err.time}] Iteration ${err.iter}: ${err.msg}</div>`).join('');
                } else {
                    errorBox.style.display = 'none';
                }
            }

            async function runIteration(engine, x, y, currentIter) {
                if (abortController.signal.aborted) return;

                try {
                    const formData = new URLSearchParams({ engine, x, y });
                    const response = await fetch('/calculate', { 
                        method: 'POST', 
                        body: formData,
                        signal: abortController.signal
                    });
                    
                    const data = await response.json();
                    
                    if (!response.ok || !data.success) {
                        throw new Error(data.detail || 'Server returned an error');
                    }

                    const comInfo = JSON.parse(data.engineJson);
                    const pid = comInfo.pid;
                    
                    if (!processStats[pid]) {
                        processStats[pid] = {
                            engine: comInfo.engine,
                            count: 0,
                            lastResult: 0,
                            firstSeen: performance.now(),
                            lastTime: new Date(),
                            memMin: Infinity, memMax: -Infinity, memLast: 0,
                            hndMin: Infinity, hndMax: -Infinity, hndLast: 0
                        };
                    }

                    const s = processStats[pid];
                    s.count++;
                    s.lastResult = data.result;
                    s.lastTime = new Date();
                    s.memLast = comInfo.memoryBytes;
                    if (s.memLast < s.memMin) s.memMin = s.memLast;
                    if (s.memLast > s.memMax) s.memMax = s.memLast;
                    s.hndLast = comInfo.handles.total;
                    if (s.hndLast < s.hndMin) s.hndMin = s.hndLast;
                    if (s.hndLast > s.hndMax) s.hndMax = s.hndLast;

                    document.getElementById('stat-engine').innerText = comInfo.engine;
                } catch (err) {
                    if (err.name !== 'AbortError') {
                        globalErrors.push({
                            time: new Date().toLocaleTimeString(),
                            iter: currentIter,
                            msg: err.message
                        });
                    }
                } finally {
                    completedIters++;
                    document.getElementById('stat-iter').innerText = completedIters + '/' + targetIters;
                    updateDisplay();
                }
            }

            document.getElementById('btn-cancel').onclick = () => {
                if (abortController) abortController.abort();
            };

            document.getElementById('calcForm').onsubmit = async (e) => {
                e.preventDefault();
                
                // Reset State
                processStats = {};
                globalErrors = [];
                completedIters = 0;
                targetIters = parseInt(document.getElementById('iters').value);
                updateDisplay();

                const concurrency = parseInt(document.getElementById('threads').value);
                const engine = document.getElementById('engine').value;
                const x = document.getElementById('x').value;
                const y = document.getElementById('y').value;

                const startBtn = document.getElementById('btn-start');
                const cancelBtn = document.getElementById('btn-cancel');
                
                startBtn.disabled = true;
                cancelBtn.style.display = 'block';
                abortController = new AbortController();
                startTime = performance.now();
                
                const timerInterval = setInterval(() => {
                    document.getElementById('stat-time').innerText = ((performance.now() - startTime) / 1000).toFixed(1) + 's';
                }, 100);

                try {
                    let nextIterId = 1;
                    const workers = Array(concurrency).fill(0).map(async () => {
                        while (nextIterId <= targetIters && !abortController.signal.aborted) {
                            const current = nextIterId++;
                            await runIteration(engine, x, y, current);
                        }
                    });

                    await Promise.all(workers);
                } catch (err) {
                    if (err.name !== 'AbortError') console.error(err);
                } finally {
                    clearInterval(timerInterval);
                    startBtn.disabled = false;
                    cancelBtn.style.display = 'none';
                    abortController = null;
                }
            };
        </script>
    </body>
    </html>";

    return Results.Content(html, "text/html");
});

app.MapPost("/calculate", async (HttpRequest request) =>
{
    try
    {
        var form = await request.ReadFormAsync();
        int x = int.Parse(form["x"]!);
        int y = int.Parse(form["y"]!);
        string engine = form["engine"]!;

        var activeProxy = engine == "delphi" ? proxyDelphi : proxyC;

        // Note: The Add method will throw if the sandbox process crashes 
        // or a timeout occurs, which will be caught below.
        var sum = activeProxy.Add(x, y);
        var info = activeProxy.GetInfo(); 

        return Results.Ok(new
        {
            Success = true,
            Result = sum,
            EngineJson = info
        });
    }
    catch (Exception ex)
    {
        // Return a 500 status code so the fetch.ok property becomes false
        return Results.Json(new { Success = false, detail = ex.Message }, statusCode: 500);
    }
});

app.Run();