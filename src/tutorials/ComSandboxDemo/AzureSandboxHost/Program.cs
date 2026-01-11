using Contracts;
using ProcessSandbox.Pool;
using ProcessSandbox.Proxy;
using System.Text.Json;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging.EventLog;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Abstractions; 
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var loggerFactory = LoggerFactory.Create(b =>
{
    b.AddConsole();
    // Add the EventLog provider
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        b.AddEventLog(new EventLogSettings
        {
            SourceName = "Application",
            LogName = "Application"
        });

        b.AddFilter<Microsoft.Extensions.Logging.EventLog.EventLogLoggerProvider>(null, LogLevel.Information);
    }
    b.SetMinimumLevel(LogLevel.Debug);
});

// ---------------------------------------------------------
// 1. Define Templates (Immutable parts: DLL paths, CLSIDs)
// ---------------------------------------------------------
var templateC = new ProcessPoolConfiguration
{
    DotNetVersion = DotNetVersion.Net48_32Bit,
    ComClsid = new Guid("11111111-2222-3333-4444-555555555555"),
    ImplementationAssemblyPath = Path.Combine(AppContext.BaseDirectory, "workers", "net48", "win-x86", "SimpleCom.dll")
};

var templateDelphi = new ProcessPoolConfiguration
{
    DotNetVersion = DotNetVersion.Net48_32Bit,
    ComClsid = new Guid("11111111-2222-3333-4444-555555555555"),
    ImplementationAssemblyPath = Path.Combine(AppContext.BaseDirectory, "workers", "SimpleComDelphi32.dll"),
    ExtraComDependencies = [
        new ComDependency
        {
            Clsid = new Guid("B1E9D2C4-8A6F-4E2B-9D3D-1234567890AB"),
            DllPath = Path.Combine(AppContext.BaseDirectory, "workers", "ComEngineInfo32.dll")
        }
    ]
};

// Global active proxy instance
ProcessProxyFactory<ICalculator>? activeFactory = null;

app.MapGet("/", () =>
{
    var html = @"
    <!DOCTYPE html>
    <html>
    <head>
        <title>COM Sandbox Dashboard</title>
        <meta name='viewport' content='width=device-width, initial-scale=1'>
        <style>
            body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background: #f0f2f5; margin: 0; padding: 10px; color: #333; }
            .container { max-width: 1200px; margin: 0 auto; display: grid; grid-template-columns: 350px 1fr; gap: 20px; }
            @media (max-width: 900px) { .container { grid-template-columns: 1fr; } }
            .card { background: white; padding: 1.2rem; border-radius: 12px; box-shadow: 0 4px 12px rgba(0,0,0,0.08); height: fit-content; }
            h2 { margin-top: 0; font-size: 1.1rem; border-bottom: 2px solid #eee; padding-bottom: 10px; }
            label { display: block; font-size: 0.75rem; font-weight: bold; margin-top: 12px; color: #666; text-transform: uppercase; }
            input, select { width: 100%; padding: 8px; margin: 4px 0 8px; border: 1px solid #ddd; border-radius: 6px; box-sizing: border-box; font-size: 0.9rem; }
            
            .info-box { margin: 15px 0; padding: 12px; background: #e7f3ff; border-left: 4px solid #007bff; border-radius: 4px; font-size: 0.85rem; line-height: 1.4; }
            .info-header { font-weight: bold; color: #0056b3; margin-bottom: 5px; display: block; }

            details { border: 1px solid #eee; border-radius: 6px; padding: 10px; background: #fafafa; margin-bottom: 15px; }
            summary { font-weight: bold; cursor: pointer; color: #007bff; font-size: 0.9rem; }
            .config-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }

            .btn-group { display: flex; gap: 10px; margin-top: 10px; }
            button { flex: 1; padding: 12px; border: none; border-radius: 6px; cursor: pointer; font-weight: bold; transition: all 0.2s; }
            #btn-start { background: #007bff; color: white; }
            #btn-start:disabled { background: #ccc; cursor: not-allowed; }
            #btn-cancel { background: #dc3545; color: white; display: none; }
            
            .status-bar { background: #333; color: white; padding: 15px; border-radius: 8px; margin-bottom: 20px; display: flex; flex-wrap: wrap; justify-content: space-between; font-family: monospace; font-size: 0.85rem; gap: 10px; }
            .status-bar span { color: #00d4ff; font-weight: bold; }
            
            .process-group { margin-bottom: 15px; border: 1px solid #e0e0e0; border-radius: 8px; overflow: hidden; background: white; }
            .process-header { background: #f8f9fa; padding: 10px 15px; display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid #eee; flex-wrap: wrap; gap: 10px; }
            .badge { padding: 3px 8px; border-radius: 4px; font-size: 0.75rem; font-weight: bold; color: white; }
            .badge-worker { background: #007bff; }
            .badge-host { background: #6f42c1; }
            .badge-active { background: #28a745; box-shadow: 0 0 8px rgba(40,167,69,0.5); }
            
            .table-wrapper { overflow-x: auto; }
            table { width: 100%; border-collapse: collapse; font-size: 0.8rem; min-width: 450px; }
            th { text-align: left; padding: 10px; color: #888; border-bottom: 1px solid #eee; font-weight: 600; }
            td { padding: 10px; border-bottom: 1px solid #f9f9f9; }
            .result-cell { font-size: 1.2rem; font-weight: bold; color: #28a745; background: #f0f9ff; text-align: center; width: 120px; }
            
            #error-box { margin-top: 20px; display: none; padding: 15px; background: #fff1f0; border: 1px solid #ffa39e; border-radius: 8px; }
            .error-item { color: #cf1322; font-size: 0.75rem; font-family: 'Consolas', monospace; margin-bottom: 10px; padding-bottom: 10px; border-bottom: 1px dashed #ffa39e; white-space: pre-wrap; word-break: break-all; }
        </style>
    </head>
    <body>
        <div class='container'>
            <div class='card'>
                <h2>Configuration</h2>
                <form id='calcForm'>
                    <label>Engine</label>
                    <select id='engine'>
                        <option value='c'>C 32bit (SimpleCom.dll)</option>
                        <option value='delphi32'>Delphi 32bit (SimpleComDelphi32.dll)</option>
                    </select>

                    <div id='engine-info' class='info-box'></div>

                    <details>
                        <summary>Advanced Pool Settings</summary>
                        <div class='config-grid'>
                            <div><label>Min Pool</label><input type='number' id='minPoolSize' value='1' min='0' /></div>
                            <div><label>Max Pool</label><input type='number' id='maxPoolSize' value='5' min='1' /></div>
                            <div><label>Max Memory (MB)</label><input type='number' id='maxMemoryMB' value='1024' /></div>
                            <div><label>GDI Limit</label><input type='number' id='maxGdiHandles' value='1000' /></div>
                            <div><label>User Limit</label><input type='number' id='maxUserHandles' value='1000' /></div>
                            <div><label>Total Handle Limit</label><input type='number' id='maxTotalHandles' value='10000' /></div>
                            <div><label>Recycle Threshold (Calls)</label><input type='number' id='processRecycleThreshold' value='0' /></div>
                            <div><label>Max Lifetime (sec)</label><input type='number' id='maxProcessLifetime' value='3600' /></div>
                            <div><label>Start Timeout (sec)</label><input type='number' id='processStartTimeout' value='60' /></div>
                            <div><label>Call Timeout (sec)</label><input type='number' id='methodCallTimeout' value='300' /></div>
                            <div><label>Recycle Check (Calls)</label><input type='number' id='recycleCheckCalls' value='100' /></div>
                            <div><label>Recycle Check (Sec)</label><input type='number' id='recycleCheckSeconds' value='10' /></div>
                        </div>
                        <label><input type='checkbox' id='verboseWorkerLogging' style='width:auto' /> Verbose Worker Logging</label>
                    </details>

                    <label>Concurrent Threads</label>
                    <input type='number' id='threads' value='2' min='1' max='20' />
                    <label>Batch Size (Calls per Req)</label>
                    <input type='number' id='batchSize' value='10' min='1' />
                    <label>Total Iterations</label>
                    <input type='number' id='iters' value='100' min='1' />
                    
                    <label id='label-x'>Input X</label>
                    <input type='number' id='x' value='10' />
                    <label id='label-y'>Input Y</label>
                    <input type='number' id='y' value='5' />

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
                    <div>HOST PID: <span id='stat-host'>-</span></div>
                </div>
                <div id='error-box'>
                    <h3 style='margin:0 0 10px 0; color:#cf1322; font-size:0.9rem;'>Detailed Error Log</h3>
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
            let sentIters = 0;
            let totalTarget = 0;

            const engineDescriptions = {
                'c': {
                    title: 'C (Fault Injection Mode)',
                    xLabel: 'Memory Leak (MB)',
                    yLabel: 'Handle Leak (Count)',
                    text: 'This engine demonstrates sandbox resilience. It will deliberately <b>leak the specified MB and Handles</b> on every single call until the process limits are hit.'
                },
                'delphi32': {
                    title: 'Delphi (COM Integration Mode)',
                    xLabel: 'Input X',
                    yLabel: 'Input Y',
                    text: 'Demonstrates <b>Advanced COM chaining</b>. It calls <b>ADO</b> for Azure SQL connectivity and links to <b>ComEngineInfo32.dll</b> via manual in-memory registration.'
                }
            };

            function updateEngineUI() {
                const engine = document.getElementById('engine').value;
                const info = engineDescriptions[engine];
                document.getElementById('engine-info').innerHTML = `<span class='info-header'>${info.title}</span>${info.text}`;
                document.getElementById('label-x').innerText = info.xLabel;
                document.getElementById('label-y').innerText = info.yLabel;
            }

            document.getElementById('engine').addEventListener('change', updateEngineUI);
            updateEngineUI();

            function formatBytes(bytes) {
                if (!bytes) return '0 B';
                if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
                return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
            }

            function updateDisplay() {
                const container = document.getElementById('process-list');
                const errorBox = document.getElementById('error-box');
                const errorList = document.getElementById('error-list');
                
                const sortedPids = Object.keys(processStats).sort((a, b) => processStats[b].firstSeen - processStats[a].firstSeen);

                container.innerHTML = sortedPids.map(pid => {
                    const s = processStats[pid];
                    const isActive = (new Date() - s.lastTime) < 1500;
                    return `
                        <div class='process-group'>
                            <div class='process-header'>
                                <div>
                                    <span class='badge badge-worker ${isActive ? 'badge-active' : ''}'>WORKER PID ${pid}</span>
                                    <span class='badge badge-host'>HOST ${s.hostPid}</span>
                                    <strong style='margin-left:10px'>${s.engineName}</strong>
                                </div>
                                <span style='font-size:0.75rem; color:#666'>${s.lastTime.toLocaleTimeString()}</span>
                            </div>
                            <div class='table-wrapper'>
                                <table>
                                    <tr>
                                        <th>Metric</th><th>Min</th><th>Max</th><th>Last</th>
                                        <th style='text-align:center'>Calls</th>
                                        <th style='text-align:center; background:#f0f9ff'>Last Result</th>
                                    </tr>
                                    <tr>
                                        <td>Memory</td>
                                        <td>${formatBytes(s.memMin)}</td><td>${formatBytes(s.memMax)}</td><td>${formatBytes(s.memLast)}</td>
                                        <td rowspan='2' style='vertical-align:middle; text-align:center; font-size:1.3rem; font-weight:bold; border-left:1px solid #eee'>${s.count}</td>
                                        <td rowspan='2' class='result-cell'>${s.lastResult}</td>
                                    </tr>
                                    <tr>
                                        <td>Handles</td>
                                        <td>${s.hndMin}</td><td>${s.hndMax}</td><td>${s.hndLast}</td>
                                    </tr>
                                </table>
                            </div>
                        </div>
                    `;
                }).join('');

                if (globalErrors.length > 0) {
                    errorBox.style.display = 'block';
                    errorList.innerHTML = globalErrors.slice(-10).reverse().map(err => `
                        <div class='error-item'><strong>[${err.time}]</strong> ${err.msg}</div>
                    `).join('');
                }
            }

            // --- 2. Configure Pool Function ---
            async function configurePool() {
                const engine = document.getElementById('engine').value;
                const config = {
                    minPoolSize: parseInt(document.getElementById('minPoolSize').value),
                    maxPoolSize: parseInt(document.getElementById('maxPoolSize').value),
                    maxMemoryMB: parseInt(document.getElementById('maxMemoryMB').value),
                    maxGdiHandles: parseInt(document.getElementById('maxGdiHandles').value),
                    maxUserHandles: parseInt(document.getElementById('maxUserHandles').value),
                    maxTotalHandles: parseInt(document.getElementById('maxTotalHandles').value),
                    processRecycleThreshold: parseInt(document.getElementById('processRecycleThreshold').value),
                    maxProcessLifetimeSeconds: parseInt(document.getElementById('maxProcessLifetime').value),
                    processStartTimeoutSeconds: parseInt(document.getElementById('processStartTimeout').value),
                    methodCallTimeoutSeconds: parseInt(document.getElementById('methodCallTimeout').value),
                    recycleCheckCalls: parseInt(document.getElementById('recycleCheckCalls').value),
                    recycleCheckSeconds: parseInt(document.getElementById('recycleCheckSeconds').value),
                    verboseWorkerLogging: document.getElementById('verboseWorkerLogging').checked
                };

                const response = await fetch('/configure?engine=' + engine, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(config)
                });

                if (!response.ok) {
                    const err = await response.json();
                    throw new Error('Configuration Failed: ' + (err.detail || response.statusText));
                }
            }

            async function runBatch(engine, x, y, requestedBatchSize) {
                if (abortController.signal.aborted) return;
                const remaining = totalTarget - sentIters;
                if (remaining <= 0) return;

                const batchToRequest = Math.min(requestedBatchSize, remaining);
                sentIters += batchToRequest; 

                try {
                    const formData = new URLSearchParams({ engine, x, y, batchSize: batchToRequest });
                    const response = await fetch('/calculate', { method: 'POST', body: formData, signal: abortController.signal });
                    const data = await response.json();
                    
                    if (!data.success) throw new Error(data.detail || 'Server logic failure');

                    data.results.forEach(item => {
                        let comInfo = JSON.parse(item.engineJson);
                        const pid = comInfo.pid;
                        document.getElementById('stat-host').innerText = data.hostPid;

                        if (!processStats[pid]) {
                            processStats[pid] = {
                                engineName: comInfo.engine, hostPid: data.hostPid, count: 0, lastResult: '',
                                firstSeen: performance.now(), lastTime: new Date(),
                                memMin: Infinity, memMax: -Infinity, memLast: 0,
                                hndMin: Infinity, hndMax: -Infinity, hndLast: 0
                            };
                        }

                        const s = processStats[pid];
                        s.count++;
                        s.lastResult = item.result;
                        s.lastTime = new Date();
                        s.memLast = comInfo.memoryBytes;
                        if (s.memLast < s.memMin) s.memMin = s.memLast;
                        if (s.memLast > s.memMax) s.memMax = s.memLast;
                        s.hndLast = comInfo.handles;
                        if (s.hndLast < s.hndMin) s.hndMin = s.hndLast;
                        if (s.hndLast > s.hndMax) s.hndMax = s.hndLast;
                        
                        completedIters++;
                    });

                    document.getElementById('stat-iter').innerText = completedIters + '/' + totalTarget;
                    updateDisplay();
                } catch (err) {
                    if (err.name !== 'AbortError') {
                        globalErrors.push({ time: new Date().toLocaleTimeString(), msg: err.message });
                        updateDisplay();
                    }
                }
            }

            document.getElementById('calcForm').onsubmit = async (e) => {
                e.preventDefault();
                processStats = {}; globalErrors = []; completedIters = 0; sentIters = 0;
                totalTarget = parseInt(document.getElementById('iters').value);
                const batchSize = parseInt(document.getElementById('batchSize').value);
                const concurrency = parseInt(document.getElementById('threads').value);
                const engine = document.getElementById('engine').value;
                const x = document.getElementById('x').value;
                const y = document.getElementById('y').value;

                document.getElementById('btn-start').disabled = true;
                document.getElementById('btn-cancel').style.display = 'block';
                document.getElementById('error-box').style.display = 'none';
                
                try {
                    // Call Configure first!
                    await configurePool();

                    abortController = new AbortController();
                    startTime = performance.now();
                    const timerInterval = setInterval(() => {
                        document.getElementById('stat-time').innerText = ((performance.now() - startTime) / 1000).toFixed(1) + 's';
                    }, 100);

                    const workers = Array(concurrency).fill(0).map(async () => {
                        while (sentIters < totalTarget && !abortController.signal.aborted) {
                            await runBatch(engine, x, y, batchSize);
                        }
                    });
                    await Promise.all(workers);
                    clearInterval(timerInterval);
                } catch (ex) {
                     alert('Run Error: ' + ex.message);
                } finally {
                    document.getElementById('btn-start').disabled = false;
                    document.getElementById('btn-cancel').style.display = 'none';
                }
            };

            document.getElementById('btn-cancel').onclick = () => { if (abortController) abortController.abort(); };
        </script>
    </body>
    </html>";

    return Results.Content(html, "text/html");
});

// ---------------------------------------------------------
// 3. New Configuration Endpoint
// ---------------------------------------------------------
app.MapPost("/configure", async (string engine, PoolSettingsModel settings) =>
{
    try
    {
        // 1. Pick the template (immutable parts)
        var template = engine == "delphi32" ? templateDelphi : templateC;

        // 2. Build the final config from Template + User Settings
        var finalConfig = new ProcessPoolConfiguration
        {
            // Immutable from template
            DotNetVersion = template.DotNetVersion,
            ComClsid = template.ComClsid,
            ImplementationAssemblyPath = template.ImplementationAssemblyPath,
            ExtraComDependencies = template.ExtraComDependencies,

            // Mutable from UI
            MinPoolSize = settings.MinPoolSize,
            MaxPoolSize = settings.MaxPoolSize,
            MaxMemoryMB = settings.MaxMemoryMB,
            MaxGdiHandles = settings.MaxGdiHandles,
            MaxUserHandles = settings.MaxUserHandles,
            MaxTotalHandles = settings.MaxTotalHandles,
            ProcessRecycleThreshold = settings.ProcessRecycleThreshold,
            VerboseWorkerLogging = settings.VerboseWorkerLogging,
            RecycleCheckCalls = settings.RecycleCheckCalls,
            RecycleCheckSeconds = settings.RecycleCheckSeconds,

            // TimeSpans
            MaxProcessLifetime = TimeSpan.FromSeconds(settings.MaxProcessLifetimeSeconds),
            ProcessStartTimeout = TimeSpan.FromSeconds(settings.ProcessStartTimeoutSeconds),
            MethodCallTimeout = TimeSpan.FromSeconds(settings.MethodCallTimeoutSeconds),
        };

        // 3. Re-Initialize the Proxy
        // If there was an old one, dispose it
        if (activeFactory is IDisposable oldFactory)
        {
            oldFactory.Dispose();
        }

        activeFactory = await ProcessProxyFactory<ICalculator>.CreateAsync(finalConfig, loggerFactory);

        return Results.Ok();
    }
    catch (Exception ex)
    {
        return Results.Json(new { detail = FlattenException(ex) }, statusCode: 500);
    }
});


// ---------------------------------------------------------
// 4. Calculate Endpoint (Logic Preserved)
// ---------------------------------------------------------
app.MapPost("/calculate", async (HttpRequest request) =>
{
    if (activeFactory == null)
        return Results.Json(new { Success = false, detail = "Proxy not configured. Call /configure first." }, statusCode: 400);

    try
    {
        var form = await request.ReadFormAsync();
        int x = int.Parse(form["x"]!);
        int y = int.Parse(form["y"]!);
        int batchSize = int.Parse(form["batchSize"]!);
        string engine = form["engine"]!;

        var batchResults = new List<object>();

        for (int i = 0; i < batchSize; i++)
        {
            try
            {
                await activeFactory.UseProxyAsync(async proxy =>
                {
                    var sum = proxy.Add(x, y);
                    var info = proxy.GetInfo();
                    batchResults.Add(new { Result = sum, EngineJson = info });
                });
            }
            catch (Exception ex)
            {
                // THIS IS THE SANDBOX PROTECTING YOU
                // The worker died mid-batch. We report it and stop the batch.
                batchResults.Add(new
                {
                    Result = "CRASH",
                    EngineJson = JsonSerializer.Serialize(new
                    {
                        engine,
                        pid = -1,
                        memoryBytes = 0,
                        handles = 0,
                        error = FlattenException(ex)
                    })
                });
            }
        }

        return Results.Ok(new
        {
            Success = true,
            HostPid = Environment.ProcessId,
            Results = batchResults
        });
    }
    catch (Exception ex)
    {
        // Recursively capture message and stack trace
        return Results.Json(new
        {
            Success = false,
            detail = FlattenException(ex)
        }, statusCode: 500);
    }
});

// Helper to get full stack trace and inner exceptions
string FlattenException(Exception ex)
{
    var sb = new StringBuilder();
    var current = ex;
    int depth = 0;
    while (current != null)
    {
        sb.AppendLine($"[Level {depth}] {current.GetType().Name}: {current.Message}");
        sb.AppendLine(current.StackTrace);
        sb.AppendLine(new string('-', 20));
        current = current.InnerException;
        depth++;
    }
    return sb.ToString();
}

app.Run();


/// <summary>
/// Model for Pool Settings from UI
/// </summary>
public class PoolSettingsModel
{
    /// <summary>
    /// Minimum number of processes in the pool
    /// </summary>
    public int MinPoolSize { get; set; }
    /// <summary>
    /// Maximum number of processes in the pool
    /// </summary>
    public int MaxPoolSize { get; set; }
    /// <summary>
    /// Maximum memory per process in MB
    /// </summary>
    public long MaxMemoryMB { get; set; }
    /// <summary>
    /// Maximum GDI handles per process
    /// </summary>
    public int MaxGdiHandles { get; set; }
    /// <summary>
    /// Maximum User handles per process
    /// </summary>
    public int MaxUserHandles { get; set; }
    /// <summary>
    /// Maximum Total handles per process
    /// </summary>
    public int MaxTotalHandles { get; set; }
    /// <summary>
    /// Number of method calls before recycling a process
    /// </summary>
    public int ProcessRecycleThreshold { get; set; }
    /// <summary>
    /// Maximum lifetime of a process in seconds
    /// </summary>
    public int MaxProcessLifetimeSeconds { get; set; }
    /// <summary>
    /// Timeout for process start in seconds
    /// </summary>
    public int ProcessStartTimeoutSeconds { get; set; }
    /// <summary>
    /// Timeout for method calls in seconds
    /// </summary>
    public int MethodCallTimeoutSeconds { get; set; }
    /// <summary>
    /// Number of calls between recycle checks
    /// </summary>
    public int RecycleCheckCalls { get; set; }
    /// <summary>
    /// Number of seconds between recycle checks
    /// </summary>
    public int RecycleCheckSeconds { get; set; }
    /// <summary>
    /// Enable verbose logging in the worker processes
    /// </summary>
    public bool VerboseWorkerLogging { get; set; }
}