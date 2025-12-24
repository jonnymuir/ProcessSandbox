using Contracts;
using ProcessSandbox.Pool;
using ProcessSandbox.Proxy;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var loggerFactory = LoggerFactory.Create(b => {
    b.AddConsole();
    b.SetMinimumLevel(LogLevel.Debug);
});

// Setup Proxies
var configBase = new ProcessPoolConfiguration {
    DotNetVersion = DotNetVersion.Net48_32Bit,
    ComClsid = new Guid("11111111-2222-3333-4444-555555555555")
};

var proxyC = await ProcessProxy.CreateAsync<ICalculator>(configBase with {
    ImplementationAssemblyPath = Path.Combine(AppContext.BaseDirectory, "workers", "net48", "win-x86", "SimpleCom.dll")
}, loggerFactory);

var proxyDelphi = await ProcessProxy.CreateAsync<ICalculator>(configBase with {
    ImplementationAssemblyPath = Path.Combine(AppContext.BaseDirectory, "workers", "net48", "win-x86", "SimpleComDelphi.dll")
}, loggerFactory);

app.MapGet("/", () => {
    var html = @"
    <!DOCTYPE html>
    <html>
    <head>
        <title>COM Sandbox Dashboard</title>
        <style>
            body { font-family: 'Segoe UI', sans-serif; background: #f4f7f9; padding: 20px; display: flex; gap: 20px; }
            .panel { background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
            .controls { width: 300px; }
            .dashboard { flex-grow: 1; }
            input, select, button { width: 100%; padding: 10px; margin: 8px 0; border: 1px solid #ddd; border-radius: 4px; box-sizing: border-box; }
            button { font-weight: bold; cursor: pointer; border: none; transition: 0.2s; }
            .btn-start { background: #007bff; color: white; }
            .btn-cancel { background: #dc3545; color: white; display: none; }
            .stats-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 15px; margin-top: 20px; }
            .process-card { border: 1px solid #eee; padding: 15px; border-radius: 6px; background: #fff; position: relative; }
            .process-card.active { border-color: #28a745; background: #f8fff9; box-shadow: 0 0 8px rgba(40,167,69,0.2); }
            .pid-badge { position: absolute; top: 10px; right: 10px; background: #333; color: #fff; padding: 2px 8px; border-radius: 4px; font-size: 0.7rem; }
            .metric-row { display: flex; justify-content: space-between; font-size: 0.85rem; margin: 4px 0; border-bottom: 1px solid #f9f9f9; }
            .metric-label { color: #666; }
            .metric-val { font-weight: bold; }
            #global-status { margin-bottom: 20px; font-size: 0.9rem; color: #555; }
        </style>
    </head>
    <body>
        <div class='panel controls'>
            <h3>Controller</h3>
            <form id='calcForm'>
                <label>Engine</label>
                <select id='engine'><option value='c'>C Native</option><option value='delphi'>Delphi</option></select>
                <label>Iterations</label>
                <input type='number' id='iters' value='50' min='1' />
                <label>Delay (ms)</label>
                <input type='number' id='delay' value='100' min='0' />
                <button type='submit' id='startBtn' class='btn-start'>Run Batch</button>
                <button type='button' id='cancelBtn' class='btn-cancel'>Cancel Run</button>
            </form>
        </div>

        <div class='panel dashboard'>
            <div id='global-status'>Ready.</div>
            <div id='processList' class='stats-grid'></div>
        </div>

        <script>
            let isRunning = false;
            let processData = {};
            let currentPid = null;

            const form = document.getElementById('calcForm');
            const startBtn = document.getElementById('startBtn');
            const cancelBtn = document.getElementById('cancelBtn');

            const formatMem = (b) => (b / 1024 / 1024).toFixed(2) + ' MB';

            function updateUI(globalInfo) {
                document.getElementById('global-status').innerText = globalInfo;
                const container = document.getElementById('processList');
                
                // Sort by last seen, with currentPid at top
                const sortedPids = Object.keys(processData).sort((a, b) => {
                    if (a == currentPid) return -1;
                    if (b == currentPid) return 1;
                    return processData[b].lastSeen - processData[a].lastSeen;
                });

                container.innerHTML = sortedPids.map(pid => {
                    const p = processData[pid];
                    return `
                        <div class='process-card ${pid == currentPid ? 'active' : ''}'>
                            <span class='pid-badge'>PID: ${pid}</span>
                            <div style='font-weight:bold; margin-bottom:10px;'>${p.engine}</div>
                            <div class='metric-row'><span class='metric-label'>Calls:</span><span class='metric-val'>${p.count}</span></div>
                            <div class='metric-row'><span class='metric-label'>Last Seen:</span><span class='metric-val'>${p.lastSeen.toLocaleTimeString()}</span></div>
                            <div class='metric-row'><span class='metric-label'>Memory (Min/Max/Last):</span><span class='metric-val'>${formatMem(p.mem.min)} / ${formatMem(p.mem.max)} / ${formatMem(p.mem.last)}</span></div>
                            <div class='metric-row'><span class='metric-label'>Handles (Min/Max/Last):</span><span class='metric-val'>${p.hnd.min} / ${p.hnd.max} / ${p.hnd.last}</span></div>
                        </div>
                    `;
                }).join('');
            }

            form.onsubmit = async (e) => {
                e.preventDefault();
                isRunning = true;
                startBtn.disabled = true;
                cancelBtn.style.display = 'block';
                
                const iterations = parseInt(document.getElementById('iters').value);
                const delay = parseInt(document.getElementById('delay').value);
                const startTime = Date.now();

                for (let i = 1; i <= iterations && isRunning; i++) {
                    try {
                        const res = await fetch('/calculate', {
                            method: 'POST',
                            headers: {'Content-Type': 'application/x-www-form-urlencoded'},
                            body: new URLSearchParams({ 
                                engine: document.getElementById('engine').value,
                                x: Math.floor(Math.random() * 100), 
                                y: Math.floor(Math.random() * 100) 
                            })
                        });
                        
                        const data = await res.json();
                        const stats = JSON.parse(data.stats); // Your new BSTR JSON
                        const pid = stats.pid;
                        currentPid = pid;

                        if (!processData[pid]) {
                            processData[pid] = { 
                                engine: stats.engine, count: 0, lastSeen: null,
                                mem: { min: Infinity, max: 0, last: 0 },
                                hnd: { min: Infinity, max: 0, last: 0 }
                            };
                        }

                        const p = processData[pid];
                        p.count++;
                        p.lastSeen = new Date();
                        
                        // Memory tracking
                        p.mem.last = stats.memoryBytes;
                        p.mem.min = Math.min(p.mem.min, stats.memoryBytes);
                        p.mem.max = Math.max(p.mem.max, stats.memoryBytes);
                        
                        // Handles tracking
                        p.hnd.last = stats.handles.total;
                        p.hnd.min = Math.min(p.hnd.min, stats.handles.total);
                        p.hnd.max = Math.max(p.hnd.max, stats.handles.total);

                        const elapsed = ((Date.now() - startTime) / 1000).toFixed(1);
                        updateUI(`Running... Iteration: ${i}/${iterations} | Elapsed: ${elapsed}s`);
                        
                        if (delay > 0) await new Promise(r => setTimeout(r, delay));

                    } catch (err) {
                        console.error(err);
                        isRunning = false;
                    }
                }

                isRunning = false;
                startBtn.disabled = false;
                cancelBtn.style.display = 'none';
                updateUI('Run Complete.');
            };

            cancelBtn.onclick = () => { isRunning = false; };
        </script>
    </body>
    </html>";
    return Results.Content(html, "text/html");
});

app.MapPost("/calculate", async (HttpRequest request) => {
    var form = await request.ReadFormAsync();
    int x = int.Parse(form["x"]!);
    int y = int.Parse(form["y"]!);
    var proxy = form["engine"] == "delphi" ? proxyDelphi : proxyC;

    var sum = proxy.Add(x, y);
    var stats = proxy.GetInfo(); // This is the JSON string from COM

    return Results.Ok(new { success = true, result = sum, stats = stats });
});

app.Run();