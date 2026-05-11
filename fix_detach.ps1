
$vmPath = "src\CctvVms.App\ViewModels\LiveViewViewModel.cs"
$vm = [System.IO.File]::ReadAllLines($vmPath)
$disposeIdx = -1
for ($i = 0; $i -lt $vm.Length; $i++) { if ($vm[$i] -match "public void Dispose") { $disposeIdx = $i; break } }
$newMethod = "    public async Task RebindFromEngineAsync()","    {","        foreach (var tile in Tiles.Where(t => !string.IsNullOrWhiteSpace(t.CameraId) && t.MediaPlayer is null))","        {","            var stream = _streamEngine.GetActiveStreams().FirstOrDefault(s => s.CameraId == tile.CameraId);","            if (stream?.MediaPlayer is null) continue;","            tile.MediaPlayer = stream.MediaPlayer;","            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);","        }","    }",""
$result = $vm[0..($disposeIdx-1)] + $newMethod + $vm[$disposeIdx..($vm.Length-1)]
[System.IO.File]::WriteAllLines($vmPath, $result, [System.Text.UTF8Encoding]::new($false))
Write-Host "Done: $($result.Length) lines, disposeIdx=$disposeIdx"

