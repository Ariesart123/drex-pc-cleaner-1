namespace Drex;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Portable mode: --portable or running from same dir as executable
        bool portable = args.Contains("--portable");
        if (portable)
        {
            string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? Application.ExecutablePath) ?? "";
            Settings.SetPortableDirectory(exeDir);
        }

        if (args.Contains("--autoclean")) { HeadlessClean(); return; }
        try
        {
            string mutexName = Settings.IsPortable ? "DREX_PC_CLEANER_SINGLE_PORTABLE" : "DREX_PC_CLEANER_SINGLE";
            var mutex = new Mutex(true, mutexName, out bool first);
            if (!first)
            {
                if (args.Contains("--elevated"))
                {
                    try { first = mutex.WaitOne(8000); } catch (AbandonedMutexException) { first = true; }
                }
                if (!first)
                {
                    MessageBox.Show("DREX PC CLEANER is already running.\nCheck Task Manager if you cannot see it.", "DREX PC CLEANER");
                    return;
                }
            }
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => Report(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception);
            Application.Run(new MainForm(args.Contains("--tray")));
        }
        catch (Exception ex) { Report(ex); }
    }

    // Used by the optional "Weekly junk cleanup" scheduled task: no window, same safe rules as Auto Clean.
    static void HeadlessClean()
    {
        try
        {
            var s = Settings.Load(); var age = TimeSpan.FromHours(Math.Max(0, s.MinAgeHours));
            long bytes = 0; int del = 0, skip = 0;
            foreach (var c in Cleaner.SafeCategories())
            {
                var r = Cleaner.Clean(c, age, CancellationToken.None);
                bytes += r.Bytes; del += r.Deleted; skip += r.Skipped;
            }
            History.Add($"Scheduled clean: freed {Fmt.Size(bytes)}, {del} deleted, {skip} skipped (in use)");
        }
        catch (Exception ex) { History.Add("Scheduled clean failed: " + ex.Message); }
    }

    static void Report(Exception? ex)
    {
        string m = ex?.ToString() ?? "Unknown error";
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "DREX_crash.txt"), m); } catch { }
        MessageBox.Show(m, "DREX PC CLEANER - error");
    }
}
