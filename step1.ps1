$p = 'src\CctvVms.App\ViewModels\LiveViewViewModel.cs'
$c = [System.IO.File]::ReadAllText($p)

# 1. Make ApplyLayoutAsync internal
$c = $c -replace 'private async Task ApplyLayoutAsync\(', 'internal async Task ApplyLayoutAsync('

# 2. Add SeedTileMetadata before RebindFromEngineAsync
$insert = @'
    public void SeedTileMetadata(IReadOnlyList<VideoTileViewModel> sourceTiles)
    {
        for (var i = 0; i < Math.Min(Tiles.Count, sourceTiles.Count); i++)
        {
            var src = sourceTiles[i];
            var dst = Tiles[i];
            dst.CameraId     = src.CameraId;
            dst.CameraName   = src.CameraName;
            dst.CameraStatus = src.CameraStatus;
            dst.StreamType   = src.StreamType;
        }
    }

'@
$c = $c -replace '(\s+public async Task RebindFromEngineAsync)', "$insert$1"

[System.IO.File]::WriteAllText($p, $c, [System.Text.UTF8Encoding]::new($false))
Write-Host 'step1 done'
