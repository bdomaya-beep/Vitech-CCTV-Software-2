using System;
using System.IO;
using System.Text;

var appPath = @"src\CctvVms.App\App.xaml.cs";
var mvmPath = @"src\CctvVms.App\ViewModels\MainViewModel.cs";
var lvmPath = @"src\CctvVms.App\ViewModels\LiveViewViewModel.cs";

// ── Fix 1: App.xaml.cs ────────────────────────────────────────────────────
var app = File.ReadAllLines(appPath);
bool f1 = false;
for (int i = 0; i < app.Length; i++)
{
    if (app[i].Contains("var stableOptions"))
    {
        // Replace lines i..i+7 (options block + closing) with new options
        var before = app[0..(i-1)];
        int closeIdx = i;
        for (int j = i; j < app.Length; j++) { if (app[j].Contains("return new LibVLC(stableOptions)")) { closeIdx = j; break; } }
        var after = app[(closeIdx+1)..];
        var newLines = new string[]
        {
            "var options = new[]",
            "{",
            "\"--rtsp-tcp\",",
            "\"--network-caching=400\",",
            "\"--live-caching=400\",",
            "\"--file-caching=400\",",
            "\"--clock-jitter=0\",",
            "\"--no-stats\",",
            "\"--no-osd\",",
            "\"--no-spu\",",
            "\"--drop-late-frames\",",
            "\"--skip-frames\",",
            "};",
            "try { return new LibVLC(options); }",
        };
        var combined = new string[before.Length + newLines.Length + after.Length];
        Array.Copy(before, combined, before.Length);
        Array.Copy(newLines, 0, combined, before.Length, newLines.Length);
        Array.Copy(after, 0, combined, before.Length + newLines.Length, after.Length);
        File.WriteAllLines(appPath, combined, new UTF8Encoding(false));
        Console.WriteLine("Fix 1 applied");
        f1 = true;
        break;
    }
}
if (!f1) Console.WriteLine("Fix 1 SKIPPED");

// ── Fix 2: MainViewModel.cs ────────────────────────────────────────────────
var mvm = File.ReadAllLines(mvmPath);
bool f2 = false;
for (int i = 0; i < mvm.Length; i++)
{
    if (mvm[i].Contains("new StreamPoolManager(_libVlc, _streamOptions, new GpuLoadBalancer"))
    {
        // find closing line (engine = new StreamEngine)
        int engineLine = i;
        for (int j = i; j < mvm.Length && j < i+5; j++) { if (mvm[j].Contains("new StreamEngine")) { engineLine = j; break; } }
        var before = mvm[0..(i-1)];
        var after = mvm[(engineLine+1)..];
        var newLines = new string[]
        {
            "        var activeCamCount = Math.Max(4, LiveView.Tiles.Count(t => !string.IsNullOrWhiteSpace(t.CameraId)));",
            "        var secondaryOpts = new StreamEngineOptions",
            "        {",
            "            MaxActiveDecoders     = activeCamCount,",
            "            MaxMainStreams         = 1,",
            "            HealthCheckInterval   = TimeSpan.FromSeconds(20),",
            "            StaleSessionThreshold = TimeSpan.FromSeconds(60)",
            "        };",
            "        var pool = await Task.Run(() => new StreamPoolManager(_libVlc, secondaryOpts, new GpuLoadBalancer { MaxGpuStreams = 2 }));",
            "        var engine = new StreamEngine(_libVlc, pool, secondaryOpts);",
        };
        var combined = new string[before.Length + newLines.Length + after.Length];
        Array.Copy(before, combined, before.Length);
        Array.Copy(newLines, 0, combined, before.Length, newLines.Length);
        Array.Copy(after, 0, combined, before.Length + newLines.Length, after.Length);
        File.WriteAllLines(mvmPath, combined, new UTF8Encoding(false));
        Console.WriteLine("Fix 2 applied");
        f2 = true;
        break;
    }
}
if (!f2) Console.WriteLine("Fix 2 SKIPPED");

// ── Fix 3: LiveViewViewModel.cs ───────────────────────────────────────────
var lvm = File.ReadAllLines(lvmPath);
bool f3 = false;
for (int i = 0; i < lvm.Length; i++)
{
    if (lvm[i].Contains("var results = await Task.WhenAll(activeTiles.Select(tile => Task.Run(async () =>"))
    {
        // Check if connectGate already present
        if (i > 0 && lvm[i-1].Contains("connectGate")) { Console.WriteLine("Fix 3 SKIPPED (already applied)"); f3=true; break; }
        var before = lvm[0..(i-1)];
        var after = lvm[i..];
        var newLine = new string[] { "        using var connectGate = new SemaphoreSlim(4, 4);" };
        var combined = new string[before.Length + newLine.Length + after.Length];
        Array.Copy(before, combined, before.Length);
        Array.Copy(newLine, 0, combined, before.Length, newLine.Length);
        Array.Copy(after, 0, combined, before.Length + newLine.Length, after.Length);
        
        // Also inject connectGate.WaitAsync inside the lambda (after camera null check)
        string text = string.Join("\n", combined);
        text = text.Replace(
            "            if (camera is null) return (tile: tile, info: (ActiveStreamInfo?)null);\n            try\n            {\n                var info = await _streamEngine.StartStreamAsync",
            "            if (camera is null) return (tile: tile, info: (ActiveStreamInfo?)null);\n            await connectGate.WaitAsync();\n            try\n            {\n                var info = await _streamEngine.StartStreamAsync");
        text = text.Replace(
            "            catch { return (tile: tile, info: (ActiveStreamInfo?)null); }\n        })));",
            "            catch { return (tile: tile, info: (ActiveStreamInfo?)null); }\n            finally { connectGate.Release(); }\n        })));");
        File.WriteAllText(lvmPath, text, new UTF8Encoding(false));
        Console.WriteLine("Fix 3 applied");
        f3 = true;
        break;
    }
}
if (!f3) Console.WriteLine("Fix 3 SKIPPED");
