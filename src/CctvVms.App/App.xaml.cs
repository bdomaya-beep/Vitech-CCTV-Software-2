using FFmpeg.AutoGen;
using System.Windows;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using CctvVms.App.ViewModels;
using CctvVms.Core.Contracts;
using CctvVms.Core.Discovery;
using CctvVms.Core.Streaming;
using CctvVms.Data;
using Microsoft.Extensions.DependencyInjection;

namespace CctvVms.App;

public partial class App : Application
{
	private ServiceProvider? _serviceProvider;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

DispatcherUnhandledException += (_, ex) =>
{
var log = Path.Combine(AppContext.BaseDirectory, "crash_ui.log");
File.WriteAllText(log, $"{ex.Exception.GetType()}: {ex.Exception.Message}\n{ex.Exception.StackTrace}");
ex.Handled = true; // keep app alive; show error in title bar instead of crashing
};
AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
{
var log = Path.Combine(AppContext.BaseDirectory, "crash_bg.log");
File.WriteAllText(log, ex.ExceptionObject.ToString() ?? "unknown");
};

		try
		{			Directory.SetCurrentDirectory(AppContext.BaseDirectory);
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
			var logPath = Path.Combine(AppContext.BaseDirectory, "startup_error.log");
			File.WriteAllText(logPath, $"{innerEx.GetType().FullName}: {innerEx.Message}\n\n{innerEx.StackTrace}\n\nOuter: {ex.Message}");
			MessageBox.Show($"Startup Error: {innerEx.Message}\n\n{innerEx.StackTrace}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
			Shutdown(1);
		}
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
			MaxActiveDecoders = 16,
			MaxMainStreams = 2,
			HealthCheckInterval = TimeSpan.FromSeconds(8),
			StaleSessionThreshold = TimeSpan.FromSeconds(30)
		});		services.AddSingleton<IStreamEngine>(sp => new StreamEngine(sp.GetRequiredService<StreamEngineOptions>()));

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
// SettingsViewModel needs StreamEngineOptions injected (already registered as singleton above)
		services.AddSingleton<MainViewModel>();

		services.AddSingleton<MainWindow>();
	}
}





