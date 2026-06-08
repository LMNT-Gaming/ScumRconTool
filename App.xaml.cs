using System.Windows;
using ScumRconTool.Services;

namespace ScumRconTool;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            AppLogService.WriteException("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(args.Exception.Message, "Red Raven Rcon Tool Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                AppLogService.WriteException("UnhandledException", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogService.WriteException("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }
}
