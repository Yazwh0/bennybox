using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BitMagic.BennyBox.Android.Services;
using BitMagic.BennyBox.Android.ViewModels;
using BitMagic.BennyBox.Android.Views;
using BitMagic.BennyBox.Core.Services;
using BitMagic.BennyBox.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
#if DEBUG
using BitMagic.BennyBox.Android.Debug;
using Microsoft.Extensions.Logging;
#endif

namespace BitMagic.BennyBox.Android;

// Explicitly Avalonia.Application, not the Android Application class in this same namespace/file
// (Application.cs) - both are named "Application" and the local one would otherwise win.
public partial class App : Avalonia.Application
{
    public static IServiceProvider? Services { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IAppPaths, AndroidAppPaths>();
        builder.Services.AddSingleton<ICredentialProtector, AndroidCredentialProtector>();
        builder.Services.AddSingleton(_ => new Media3PlayerEngine(global::Android.App.Application.Context!));
        builder.Services.AddSingleton<IPlayerEngine>(sp => sp.GetRequiredService<Media3PlayerEngine>());
        builder.Services.AddSingleton<AndroidShellViewModel>();

        SharedServiceRegistration.AddSharedServices(builder.Services);

        var host = builder.Build();
        Services = host.Services;
        AppServices.Current = host.Services;

        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new AndroidShellView
            {
                DataContext = host.Services.GetRequiredService<AndroidShellViewModel>()
            };
        }

#if DEBUG
        var debugBridgeLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DebugBridge");
        var debugBridge = new AndroidDebugBridgeServer(host.Services.GetRequiredService<AndroidShellViewModel>(), debugBridgeLogger);
        debugBridge.Start();
#endif

        base.OnFrameworkInitializationCompleted();
    }
}
