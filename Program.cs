using System.Diagnostics;
using System.Text;

namespace KeyBar;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Global\KeyBar_SingleInstance", out bool first);
        if (!first) return;

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Task.Run(RegisterStartup);
        Application.Run(new BarForm());
    }

    // Creates a Task Scheduler entry: run at logon, highest privileges (no UAC prompt), works on battery.
    static void RegisterStartup()
    {
        try
        {
            string exe = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exe)) return;
            string path = exe.Replace("'", "''");
            string script =
                "$id=[Security.Principal.WindowsIdentity]::GetCurrent().Name;" +
                $"$a=New-ScheduledTaskAction -Execute '{path}';" +
                "$t=New-ScheduledTaskTrigger -AtLogOn -User $id;" +
                "$p=New-ScheduledTaskPrincipal -UserId $id -LogonType Interactive -RunLevel Highest;" +
                "$s=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero);" +
                "Register-ScheduledTask -TaskName 'KeyBar' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null";
            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + enc,
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch { /* ignore */ }
    }
}
