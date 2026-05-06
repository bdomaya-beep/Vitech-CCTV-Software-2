using System.Windows;
using System.IO;
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

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		LibVLCSharp.Shared.Core.Initialize();

		var services = new ServiceCollection();
		ConfigureServices(services);
		_serviceProvider = services.BuildServiceProvider();

		var store = _serviceProvider.GetRequiredService<IDataStoreService>();
		await store.InitializeAsync();

		var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
		await mainViewModel.InitializeAsync();

		var window = _serviceProvider.GetRequiredService<MainWindow>();
		window.Show();
	}

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
			MaxActiveDecoders = 12,
			MaxMainStreams = 4,
			HealthCheckInterval = TimeSpan.FromSeconds(4),
			StaleSessionThreshold = TimeSpan.FromSeconds(18)
		});

		services.AddSingleton(_ => new LibVLC(
			"--avcodec-hw=none",
			"--rtsp-tcp",
			"--network-caching=600",
			"--file-caching=300",
			"--clock-synchro=0"
		));
		services.AddSingleton<IStreamPoolManager, StreamPoolManager>();
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
}

