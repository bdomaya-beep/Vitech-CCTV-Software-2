import pathlib, re

# Fix 1: App.xaml.cs
p = pathlib.Path('src/CctvVms.App/App.xaml.cs')
t = p.read_text('utf-8')
applied = []

if '--network-caching=400' not in t:
    t = t.replace('var stableOptions', 'var options')
    t = t.replace('return new LibVLC(stableOptions)', 'return new LibVLC(options)')
    t = t.replace('"--network-caching=1000"', '"--network-caching=400"')
    t = t.replace('"--live-caching=1000"', '"--live-caching=400"')
    t = t.replace('"--file-caching=1000"', '"--file-caching=400"')
    t = re.sub(
        r'"--file-caching=400",\s*\};',
        '"--file-caching=400",\n\t\t\t"--clock-jitter=0",\n\t\t\t"--no-stats",\n\t\t\t"--no-osd",\n\t\t\t"--no-spu",\n\t\t\t"--drop-late-frames",\n\t\t\t"--skip-frames",\n\t\t};',
        t
    )
    t = t.replace(
        'try { return new LibVLC("--avcodec-hw=dxva2", "--rtsp-tcp", "--network-caching=1000"); }',
        'try { return new LibVLC("--rtsp-tcp", "--network-caching=400", "--no-stats"); }'
    )
    p.write_text(t, 'utf-8')
    applied.append('Fix 1 applied')
else:
    applied.append('Fix 1 SKIPPED (already applied)')

# Fix 2: MainViewModel.cs
p2 = pathlib.Path('src/CctvVms.App/ViewModels/MainViewModel.cs')
t2 = p2.read_text('utf-8')
old2 = '        var pool = await Task.Run(() => new StreamPoolManager(_libVlc, _streamOptions, new GpuLoadBalancer { MaxGpuStreams = 4 }));\n        var engine = new StreamEngine(_libVlc, pool, _streamOptions);'
new2 = """        var activeCamCount = Math.Max(4, LiveView.Tiles.Count(t => !string.IsNullOrWhiteSpace(t.CameraId)));
        var secondaryOpts = new StreamEngineOptions
        {
            MaxActiveDecoders     = activeCamCount,
            MaxMainStreams         = 1,
            HealthCheckInterval   = TimeSpan.FromSeconds(20),
            StaleSessionThreshold = TimeSpan.FromSeconds(60)
        };
        var pool = await Task.Run(() => new StreamPoolManager(_libVlc, secondaryOpts, new GpuLoadBalancer { MaxGpuStreams = 2 }));
        var engine = new StreamEngine(_libVlc, pool, secondaryOpts);"""

if old2 in t2:
    t2 = t2.replace(old2, new2)
    p2.write_text(t2, 'utf-8')
    applied.append('Fix 2 applied')
elif 'secondaryOpts' in t2:
    applied.append('Fix 2 SKIPPED (already applied)')
else:
    # Try with \r\n
    old2r = old2.replace('\n', '\r\n')
    if old2r in t2:
        t2 = t2.replace(old2r, new2)
        p2.write_text(t2, 'utf-8')
        applied.append('Fix 2 applied (CRLF)')
    else:
        applied.append('Fix 2 SKIPPED: pattern not found')

# Fix 3: LiveViewViewModel.cs
p3 = pathlib.Path('src/CctvVms.App/ViewModels/LiveViewViewModel.cs')
t3 = p3.read_text('utf-8')
if 'connectGate' in t3:
    applied.append('Fix 3 SKIPPED (already applied)')
else:
    # Insert connectGate line before the Task.WhenAll
    marker = '        var results = await Task.WhenAll(activeTiles.Select(tile => Task.Run(async () =>'
    if marker in t3:
        t3 = t3.replace(marker, '        using var connectGate = new SemaphoreSlim(4, 4);\n' + marker)
        # inject WaitAsync after camera null check
        old_wait = '            if (camera is null) return (tile: tile, info: (ActiveStreamInfo?)null);\n            try\n'
        new_wait = '            if (camera is null) return (tile: tile, info: (ActiveStreamInfo?)null);\n            await connectGate.WaitAsync();\n            try\n'
        t3 = t3.replace(old_wait, new_wait, 1)
        # inject finally before closing paren
        old_fin = '            catch { return (tile: tile, info: (ActiveStreamInfo?)null); }\n        })))'
        new_fin = '            catch { return (tile: tile, info: (ActiveStreamInfo?)null); }\n            finally { connectGate.Release(); }\n        }))'
        t3 = t3.replace(old_fin, new_fin, 1)
        p3.write_text(t3, 'utf-8')
        applied.append('Fix 3 applied')
    else:
        applied.append('Fix 3 SKIPPED: marker not found')

for msg in applied:
    print(msg)
import pathlib
p=pathlib.Path('src/CctvVms.App/App.xaml.cs')
t=p.read_text('utf-8')
t=t.replace('avcodec-hw=dxva2', 'no-dxva2-removed')
p.write_text(t,'utf-8')
print('dxva2 removed')
