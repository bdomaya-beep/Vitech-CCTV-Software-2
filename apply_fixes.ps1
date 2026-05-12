# ── Fix 1: VLC options in App.xaml.cs ─────────────────────────────────────
$appPath = "src\CctvVms.App\App.xaml.cs"
$app = [System.IO.File]::ReadAllText($appPath)

# Replace the entire CreateLibVlc method
$oldMethod = 'private static LibVLC CreateLibVlc()
{
var stableOptions = new[]
{
"--rtsp-tcp",
"--network-caching=1000",
                      "--live-caching=1000","--file-caching=1000",};

try
{
return new LibVLC(stableOptions);
}
catch
{
// Fallback to baseline init when a platform-specific VLC option is unsupported.
try { return new LibVLC("--avcodec-hw=dxva2", "--rtsp-tcp", "--network-caching=1000"); }
catch { return new LibVLC(); }
}
}'

$newMethod = 'private static LibVLC CreateLibVlc()
{
var options = new[]
{
"--rtsp-tcp",
"--network-caching=400",
"--live-caching=400",
"--file-caching=400",
"--clock-jitter=0",
"--no-stats",
"--no-osd",
"--no-spu",
"--drop-late-frames",
"--skip-frames",
};
try { return new LibVLC(options); }
catch
{
try { return new LibVLC("--rtsp-tcp", "--network-caching=400", "--no-stats"); }
catch { return new LibVLC(); }
}
}'

if ($app.Contains($oldMethod)) {
    $app = $app.Replace($oldMethod, $newMethod)
    [System.IO.File]::WriteAllText($appPath, $app, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Fix 1 applied: VLC options"
} else {
    Write-Host "Fix 1 SKIPPED: pattern not found"
}

# ── Fix 2: StreamEngineOptions for new live windows (MainViewModel.cs) ─────
$mvmPath = "src\CctvVms.App\ViewModels\MainViewModel.cs"
$mvm = [System.IO.File]::ReadAllText($mvmPath)

$oldNew = 'var pool = await Task.Run(() => new StreamPoolManager(_libVlc, _streamOptions, new GpuLoadBalancer { MaxGpuStreams = 4 }));
        var engine = new StreamEngine(_libVlc, pool, _streamOptions);'

$newNew = '// Use a lighter engine config for secondary windows:
        // fewer decoders (match actual tile count), slower health check (less overhead).
        var activeCamCount = Math.Max(4, LiveView.Tiles.Count(t => !string.IsNullOrWhiteSpace(t.CameraId)));
        var secondaryOpts = new StreamEngineOptions
        {
            MaxActiveDecoders    = activeCamCount,
            MaxMainStreams        = 1,
            HealthCheckInterval  = TimeSpan.FromSeconds(20),
            StaleSessionThreshold = TimeSpan.FromSeconds(60)
        };
        var pool = await Task.Run(() => new StreamPoolManager(_libVlc, secondaryOpts, new GpuLoadBalancer { MaxGpuStreams = 2 }));
        var engine = new StreamEngine(_libVlc, pool, secondaryOpts);'

if ($mvm.Contains($oldNew)) {
    $mvm = $mvm.Replace($oldNew, $newNew)
    [System.IO.File]::WriteAllText($mvmPath, $mvm, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Fix 2 applied: lighter engine for secondary windows"
} else {
    Write-Host "Fix 2 SKIPPED: pattern not found"
}

# ── Fix 3: Throttle RTSP connections in StartAllCameraStreamsAsync ──────────
$lvmPath = "src\CctvVms.App\ViewModels\LiveViewViewModel.cs"
$lvm = [System.IO.File]::ReadAllText($lvmPath)

$oldPhase1 = '        // Phase 1: acquire sessions in parallel on thread pool — pool.Acquire is in-memory,
        // but forcing off UI thread prevents synchronous SemaphoreSlim completions from
        // blocking the WPF message loop.
        var results = await Task.WhenAll(activeTiles.Select(tile => Task.Run(async () =>
        {
            var camera = _deviceTree.FindCamera(tile.CameraId);
            if (camera is null) return (tile: tile, info: (ActiveStreamInfo?)null);
            try
            {
                var info = await _streamEngine.StartStreamAsync(camera, StreamType.Sub);
                return (tile: tile, info: (ActiveStreamInfo?)info);
            }
            catch { return (tile: tile, info: (ActiveStreamInfo?)null); }
        })));'

$newPhase1 = '        // Phase 1: acquire sessions with a max-concurrency limiter.
        // Connecting all cameras simultaneously overwhelms the NVR TCP stack and causes
        // jitter in all open windows. Allow at most 4 simultaneous new connections.
        using var connectGate = new SemaphoreSlim(4, 4);
        var results = await Task.WhenAll(activeTiles.Select(tile => Task.Run(async () =>
        {
            var camera = _deviceTree.FindCamera(tile.CameraId);
            if (camera is null) return (tile: tile, info: (ActiveStreamInfo?)null);
            await connectGate.WaitAsync();
            try
            {
                var info = await _streamEngine.StartStreamAsync(camera, StreamType.Sub);
                return (tile: tile, info: (ActiveStreamInfo?)info);
            }
            catch { return (tile: tile, info: (ActiveStreamInfo?)null); }
            finally { connectGate.Release(); }
        })));'

if ($lvm.Contains($oldPhase1)) {
    $lvm = $lvm.Replace($oldPhase1, $newPhase1)
    [System.IO.File]::WriteAllText($lvmPath, $lvm, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Fix 3 applied: RTSP connection throttling"
} else {
    Write-Host "Fix 3 SKIPPED: pattern not found"
}

Write-Host "All done."
