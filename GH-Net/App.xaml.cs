using GH_Net.Repository.Abstractions;
using GH_Net.Licensing;
using GH_Net.Repository.Data;
using GH_Net.Repository.Devices;
using GH_Net.Service.Abstractions;
using GH_Net.Service.Implements;
using GH_Net.WPF.ViewModels;
using GH_Net.WPF.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using GH_Net.WPF.Views;

namespace GH_Net.WPF;

/// <summary>Application entry point and dependency injection root.</summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();
        Services.GetRequiredService<LanguageService>().Initialize();

        if (e.Args.Any(argument =>
                string.Equals(argument, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            // Published-package smoke: force DI construction and WPF XAML/resource loading
            // without touching the operator database, license storage or hardware sockets.
            _ = Services.GetRequiredService<CreateJobViewModel>();
            _ = Services.GetRequiredService<PackingMainViewModel>();
            _ = Services.GetRequiredService<TraceabilityViewModel>();
            _ = Services.GetRequiredService<PackingExportViewModel>();
            _ = Services.GetRequiredService<SettingsViewModel>();
            _ = new SettingsWindow(Services.GetRequiredService<SettingsViewModel>());
            _ = new TechnicalAccessWindow(Services.GetRequiredService<ITechnicalSettingsAccess>(), Services.GetRequiredService<LanguageService>(), setup: false);
            _ = new CreateJobWindow(Services.GetRequiredService<CreateJobViewModel>());
            _ = Services.GetRequiredService<MainWindow>();
            Shutdown(0);
            return;
        }

        try
        {
            var licenseManager = Services.GetRequiredService<ILicenseManager>();
            await licenseManager.InitializeAsync();
            if (e.Args.Any(argument =>
                    string.Equals(argument, "--request-license", StringComparison.OrdinalIgnoreCase)))
            {
                var requestViewModel = Services.GetRequiredService<LicenseViewModel>();
                new LicenseWindow(requestViewModel, activationRequired: false).ShowDialog();
                Shutdown();
                return;
            }
            if (!licenseManager.Current.IsValid)
            {
                var activationViewModel = Services.GetRequiredService<LicenseViewModel>();
                var activationWindow = new LicenseWindow(
                    activationViewModel,
                    activationRequired: true);
                if (activationWindow.ShowDialog() != true || !licenseManager.Current.IsValid)
                {
                    Shutdown(2);
                    return;
                }
            }

            using var context = Services.GetRequiredService<PrinterManagerDbContext>();
            await context.Database.MigrateAsync();
            await ConfigureSqliteForConcurrentRuntimeAsync(context);

            var jobService = Services.GetRequiredService<IJobService>();
            await jobService.RecoverCrashedJobsAsync();

            Services.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Không thể khởi động GH-Net.\n{ex.Message}",
                "Lỗi khởi động",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Drain the atomic transition and send PLC STOP before disposing device transports.
        if (Services?.GetService<IPackingRuntimeService>() is { IsRunning: true } runtime)
        {
            try { Task.Run(() => runtime.StopAsync()).GetAwaiter().GetResult(); }
            catch { /* The persisted paused/uncertain state is reconciled on the next launch. */ }
        }
        if (Services is IAsyncDisposable asyncDisposable)
        {
            Task.Run(async () => await asyncDisposable.DisposeAsync())
                .GetAwaiter()
                .GetResult();
        }
        else if (Services is IDisposable disposable)
            disposable.Dispose();

        base.OnExit(e);
    }

    public static T GetService<T>() where T : class
        => Services.GetRequiredService<T>();

    private static async Task ConfigureSqliteForConcurrentRuntimeAsync(
        PrinterManagerDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        try
        {
            var connection = context.Database.GetDbConnection();
            await using (var journal = connection.CreateCommand())
            {
                journal.CommandText = "PRAGMA journal_mode=WAL;";
                string? mode = Convert.ToString(await journal.ExecuteScalarAsync());
                if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("SQLite WAL mode could not be enabled.");
            }
            await using (var synchronous = connection.CreateCommand())
            {
                synchronous.CommandText = "PRAGMA synchronous=NORMAL;";
                await synchronous.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<PrinterManagerDbContext>(
            options => options.UseSqlite(PrinterManagerDbContext.DefaultConnectionString),
            ServiceLifetime.Transient);

        services.AddTransient<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();
        services.AddSingleton<LicenseTokenCodec>();
        services.AddSingleton<IReadOnlyDictionary<string, byte[]>>(
            _ => DesktopLicenseKeyRing.Create());
        services.AddSingleton<IHardwareIdProvider, WindowsHardwareIdProvider>();
        services.AddSingleton<ILicenseStorage, DpapiLicenseStorage>();
        services.AddSingleton<ILicenseManager, LicenseManager>();
        services.AddSingleton<IScannerDevice, SerialScannerDevice>();
        services.AddSingleton<IZebraPrinterDevice, ZebraPrinterDevice>();
        services.AddSingleton<ICameraDevice, CameraDevice>();
        services.AddSingleton<IPlcDevice, PlcDevice>();
        services.AddSingleton<ITechnicalLockStore, TechnicalLockStore>();
        services.AddSingleton<ITechnicalSettingsAccess, TechnicalSettingsAccess>();
        services.AddSingleton<IAutomationService, AutomationService>();

        services.AddSingleton<ITraceabilityService, TraceabilityService>();
        services.AddSingleton<IPackingExportService, PackingExportService>();
        services.AddTransient<IJobService, JobService>();
        services.AddSingleton<ICsvImportService, CsvImportService>();
        services.AddSingleton<IPackingJobService, PackingJobService>();
        services.AddTransient<IScannerConfigService, ScannerConfigService>();
        services.AddSingleton<IZplTemplateRenderer, ZplTemplateRenderer>();
        services.AddTransient<IZebraPrinterConfigService, ZebraPrinterConfigService>();
        services.AddSingleton<IPackingRuntimeService, PackingRuntimeService>();
        services.AddSingleton<IJobRuntimeCoordinator, JobRuntimeCoordinator>();
        services.AddSingleton<LanguageService>();

        services.AddTransient<JobsViewModel>();
        services.AddTransient<CreateJobViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<PackingMainViewModel>();
        services.AddTransient<TraceabilityViewModel>();
        services.AddTransient<PackingExportViewModel>();
        services.AddTransient<LicenseViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
