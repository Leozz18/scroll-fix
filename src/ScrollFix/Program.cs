namespace ScrollFix;

internal static class Program
{
    private static readonly Mutex SingleInstance = new(true, @"Local\ScrollFix.SingleInstance");

    [STAThread]
    private static void Main()
    {
        if (!SingleInstance.WaitOne(TimeSpan.Zero, true))
        {
            MessageBox.Show(
                "Scroll Fix is already running (check the tray near the clock).",
                "Scroll Fix",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }
}
