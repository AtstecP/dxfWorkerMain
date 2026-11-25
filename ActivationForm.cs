namespace dxfWorker;

internal sealed class ActivationForm : Form
{
    private readonly TextBox codeBox = new()
    {
        Location = new Point(20, 135), Size = new Size(440, 80), Multiline = true, ScrollBars = ScrollBars.Vertical
    };
    private readonly Label statusLabel = new()
    {
        Location = new Point(20, 222), Size = new Size(440, 40), ForeColor = Color.Firebrick
    };

    public ActivationForm(string message)
    {
        Text = "DXF Worker — активация";
        ClientSize = new Size(480, 320);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var machineBox = new TextBox
        {
            Location = new Point(20, 55), Size = new Size(200, 31), ReadOnly = true, Text = License.MachineId()
        };
        var activate = new Button { Location = new Point(20, 270), Size = new Size(140, 36), Text = "Активировать" };
        var exit = new Button { Location = new Point(320, 270), Size = new Size(140, 36), Text = "Выход", DialogResult = DialogResult.Cancel };
        activate.Click += (_, _) => Activate_Click();
        statusLabel.Text = message;
        AcceptButton = activate;
        CancelButton = exit;

        Controls.AddRange(new Control[]
        {
            new Label { AutoSize = true, Location = new Point(20, 20), Text = "ID компьютера (сообщите поставщику):" },
            machineBox,
            new Label { AutoSize = true, Location = new Point(20, 105), Text = "Код активации:" },
            codeBox, statusLabel, activate, exit
        });
    }

    private void Activate_Click()
    {
        LicenseResult r = License.Activate(codeBox.Text);
        if (r.Status == LicenseStatus.Valid) { DialogResult = DialogResult.OK; return; }
        statusLabel.Text = License.Describe(r);
    }
}
