using System.Windows;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using CctvVms.App.ViewModels;
using CctvVms.Core.Contracts;
using CctvVms.Core.Discovery;
using CctvVms.Core.Streaming;
using CctvVms.Data;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace CctvVms.App;

public partial class App : Application
{
	private ServiceProvider? _serviceProvider;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		try
		{
			var nativePath = GetLibVlcNativePath();
			Directory.SetCurrentDirectory(AppContext.BaseDirectory);
			ConfigureLibVlcNativePath();
			LibVLCSharp.Shared.Core.Initialize(nativePath);

			var services = new ServiceCollection();
			ConfigureServices(services);
			_serviceProvider = services.BuildServiceProvider();

			var store = _serviceProvider.GetRequiredService<IDataStoreService>();
			store.InitializeAsync().Wait();

			var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
			mainViewModel.InitializeAsync().Wait();

			var window = _serviceProvider.GetRequiredService<MainWindow>();
			window.Show();
		}
		catch (Exception ex)
		{
			var innerEx = ex.InnerException ?? ex;
			MessageBox.Show($"Startup Error: {innerEx.Message}\n\n{innerEx.StackTrace}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
			Shutdown(1);
		}
	}

	private static void ConfigureLibVlcNativePath()
	{
		var nativePath = GetLibVlcNativePath();
		var pluginPath = Path.Combine(nativePath, "plugins");

		if (!Directory.Exists(nativePath))
		{
			throw new DirectoryNotFoundException($"LibVLC native folder was not found: {nativePath}");
		}

		if (!Directory.Exists(pluginPath))
		{
			throw new DirectoryNotFoundException($"LibVLC plugin folder was not found: {pluginPath}");
		}

		var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		if (!currentPath.Contains(nativePath, StringComparison.OrdinalIgnoreCase))
		{
			Environment.SetEnvironmentVariable("PATH", nativePath + Path.PathSeparator + currentPath);
		}

		Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", pluginPath);

		SetDllDirectory(nativePath);
		PreloadLibVlc(nativePath);
	}

	private static string GetLibVlcNativePath()
	{
		var architectureFolder = Environment.Is64BitProcess ? "win-x64" : "win-x86";
		return Path.Combine(AppContext.BaseDirectory, "libvlc", architectureFolder);
	}

	private static void PreloadLibVlc(string nativePath)
	{
		LoadNativeLibrary(Path.Combine(nativePath, "libvlccore.dll"));
		LoadNativeLibrary(Path.Combine(nativePath, "libvlc.dll"));
	}

	private static void LoadNativeLibrary(string libraryPath)
	{
		if (!NativeLibrary.TryLoad(libraryPath, out _))
		{
			var error = Marshal.GetLastWin32Error();
			throw new Win32Exception(error, $"Failed to load native library '{libraryPath}'.");
		}
	}

	[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern bool SetDllDirectory(string lpPathName);

	protected override void OnExit(ExitEventArgs e)
	{
		_serviceProvider?.Dispose();
		base.OnExit(e);
	}

	private static void ConfigureServices(IServiceCollection services)
	{
		var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CctvVms");
		Directory.CreateDirectory(appData);

		services.AddSingleton(new StreamEngineOptions
		{
			MaxActiveDecoders = 16,
			MaxMainStreams = 2,
			HealthCheckInterval = TimeSpan.FromSeconds(8),
			StaleSessionThreshold = TimeSpan.FromSeconds(30)
		});

		services.AddSingleton(_ => CreateLibVlc());
		services.AddSingleton(_ => new GpuLoadBalancer { MaxGpuStreams = 4 });
		services.AddSingleton<IStreamPoolManager>(sp =>
		{
			var pool = new StreamPoolManager(
				sp.GetRequiredService<LibVLC>(),
				sp.GetRequiredService<StreamEngineOptions>(),
				sp.GetRequiredService<GpuLoadBalancer>());
			return pool;
		});
		// IStreamPoolManager registered above with pre-warm factory
		services.AddSingleton<IStreamEngine, StreamEngine>();

		services.AddSingleton<IDataStoreService>(_ => new SqliteDataStoreService(Path.Combine(appData, "vms.db")));

		services.AddSingleton<OnvifWsDiscoveryService>();
		services.AddSingleton<NetworkDiscoveryEngine>();
		services.AddSingleton<MultiNetworkScanner>();
		services.AddSingleton<DeviceFilter>();
		services.AddSingleton<AutoDiscoveryService>();
		services.AddSingleton<IDeviceDiscoveryService, CompositeDiscoveryService>();
		services.AddSingleton<INvrConnectionService, NvrConnectionService>();

		services.AddSingleton<DeviceTreeViewModel>();
		services.AddSingleton<LiveViewViewModel>();
		services.AddSingleton<PlaybackViewModel>();
		services.AddSingleton<DeviceManagerViewModel>();
		services.AddSingleton<SettingsViewModel>();
		services.AddSingleton<MainViewModel>();

		services.AddSingleton<MainWindow>();
	}

	private static LibVLC CreateLibVlc()
	{
		// d3d11va = Windows Direct3D 11 hardware decoder — required for HEVC streams.
		// avcodec-hw=any is the fallback if d3d11va is unavailable on this GPU.
		var stableOptions = new[]
		{
			"--avcodec-hw=d3d11va",
			"--rtsp-tcp",
			"--network-caching=500",
			"--file-caching=300",
			"--clock-synchro=0",
			"--no-ts-trust-pcr",
			"--avcodec-skiploopfilter=all",  // skip in-loop deblocking filter on CPU
			"--avcodec-skip-frame=nonref",    // only decode reference frames when lagging
			"--avcodec-skip-idct=nonref",
			"--drop-late-frames",
			"--skip-frames"
		};

		try
		{
			return new LibVLC(stableOptions);
		}
		catch
		{
			// Fallback to baseline init when a platform-specific VLC option is unsupported.
			try { return new LibVLC("--avcodec-hw=dxva2", "--rtsp-tcp", "--network-caching=500"); }
			catch { return new LibVLC(); }
		}
	}
}

