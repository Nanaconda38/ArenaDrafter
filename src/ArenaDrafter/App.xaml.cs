using System.Configuration;
using System.Data;
using System.Windows;

namespace ArenaDrafter;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        CrashSession.Start();
        DispatcherUnhandledException += (_, args) => CrashSession.RecordCrash("dispatcher", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception) CrashSession.RecordCrash("AppDomain", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => Log.Error("An unobserved task exception was collected.", args.Exception);
        base.OnStartup(e);
    }
}

