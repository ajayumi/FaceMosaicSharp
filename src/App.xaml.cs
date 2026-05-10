using System.IO;
using System.Windows;
using System.Windows.Threading;
using FaceMosaicSharp.Services;
using FaceMosaicSharp.ViewModels;
using FaceMosaicSharp.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace FaceMosaicSharp;

/// <summary>
/// 应用入口类，负责 Serilog 日志初始化、DI 容器配置、服务注册和全局异常处理
/// </summary>
public partial class App : Application
{
    /// <summary>全局依赖注入容器</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>应用启动：配置 Serilog 日志、注册 DI 服务、显示主窗口</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        var logPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "logs",
            "FaceMosaicSharp-.log");

        var template = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: template)
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 20, outputTemplate: template)
            .CreateLogger();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddDebug();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
            builder.AddSerilog(dispose: true);
        });

        services.AddKeyedSingleton<IFaceDetectionService, YoloFaceDetectionService>(FaceDetectionMethod.Yolo);
        services.AddSingleton<IFaceParsingService, FaceParsingService>();
        services.AddSingleton<IVideoHistoryService, VideoHistoryService>();
        services.AddTransient<IVideoProcessingService, VideoProcessingService>();
        services.AddSingleton<IDialogService, WpfUiDialogService>();
        
        // Register ViewModels and Views for Dependency Injection
        services.AddTransient<ReviewFramesViewModel>();
        services.AddTransient<ReviewFramesView>();
        services.AddTransient<FaceEditorViewModel>();
        services.AddTransient<FaceEditorView>();

        services.AddTransient<MainViewModel>();
        services.AddTransient<MainView>();
        services.AddTransient<MainWindow>();

        Services = services.BuildServiceProvider();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    /// <summary>应用退出时刷新并关闭 Serilog 日志</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled exception");
        System.Windows.MessageBox.Show(
            $"An unexpected error occurred: {e.Exception.Message}",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}