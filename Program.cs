namespace dxfWorker;
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Ask for an activation code until the license is valid (or the user quits).
        LicenseResult license = License.Check();
        while (license.Status != LicenseStatus.Valid)
        {
            using var dialog = new ActivationForm(License.Describe(license));
            if (dialog.ShowDialog() != DialogResult.OK) return;
            license = License.Check();
        }

        // Re-check while the app is running, so the license also ends mid-session.
        var timer = new System.Windows.Forms.Timer { Interval = 10_000 };
        timer.Tick += (_, _) =>
        {
            LicenseResult r = License.Check();
            if (r.Status == LicenseStatus.Valid) return;
            timer.Stop();
            MessageBox.Show(License.Describe(r) + "\nПрограмма будет закрыта.", "DXF Worker",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Application.Exit();
        };
        timer.Start();

        Application.Run(new Form1());
    }
}
