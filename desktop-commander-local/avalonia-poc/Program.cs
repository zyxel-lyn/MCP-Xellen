using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using McpXellen.AvaloniaPoc;

namespace McpXellen.AvaloniaPoc;

public static class Program
{
    public static int Main(string[] args)
    {
        var headlessTest = args.Contains("--headless-test", StringComparer.OrdinalIgnoreCase);
        var builder = AppBuilder.Configure<PocApp>();
        if (headlessTest)
            builder.UseHeadless(new AvaloniaHeadlessPlatformOptions());
        else
            builder.UsePlatformDetect();
        builder.With(new Win32PlatformOptions());
        builder.LogToTrace();

        if (headlessTest)
        {
            builder.SetupWithoutStarting();
            return HeadlessTest.Run();
        }

        // Interactive proof: manual resize/multi-monitor/DPI checks by the owner.
        return builder.StartWithClassicDesktopLifetime(args);
    }
}

public sealed class PocApp : Application
{
    public override void Initialize()
    {
        Name = "McpXellenAvaloniaPoc";
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new ActivityMonitorViewModel();
            desktop.MainWindow = new MainWindow(vm);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
