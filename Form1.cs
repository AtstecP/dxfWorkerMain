namespace dxfWorker;

public class Form1 : Form
{
    private readonly Label label1 = new() { AutoSize = true, Location = new Point(20, 307) };
    private readonly Button UN = MakeButton("UN", 58);
    private readonly Button U = MakeButton("U", 98);
    private readonly Button T = MakeButton("T", 138);
    private readonly Button PLas = MakeButton("PL", 175);
    private readonly Button KL = MakeButton("KL", 18);
    private readonly Button set_folder_button = MakeButton("Select folder", 252);
    private readonly Label label2 = new() { AutoSize = true, Location = new Point(20, 347) };
    private readonly TextBox textBox1 = new() { Location = new Point(113, 215), Size = new Size(96, 31) };
    private readonly Button buton_Enter = new() { Location = new Point(215, 212), Size = new Size(72, 36), Text = "Enter" };

    public Form1()
    {
        Text = "Form1";
        ClientSize = new Size(401, 420);
        label1.Click += label1_Click;
        foreach (Button button in new[] { UN, U, T, PLas, KL }) button.Click += change_Folder;
        set_folder_button.Click += set_Folder;
        buton_Enter.Click += button_Enter;
        Controls.AddRange(new Control[] { buton_Enter, textBox1, label2, set_folder_button, KL, PLas, T, U, UN, label1 });
        label1.Text = "Copy and click";
        if (Clipboard.GetText().Length > 0) label1.Text = Clipboard.GetText();
        label2.Text = "";
    }

    private static Button MakeButton(string text, int y) => new() { Location = new Point(113, y), Size = new Size(174, 36), Text = text, UseVisualStyleBackColor = true };
    private void copy_Folder(string sourse, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string file in Directory.GetFiles(sourse)) File.Copy(file, dest + "\\" + Path.GetFileName(file), false);
        foreach (string dir in Directory.GetDirectories(sourse)) copy_Folder(dir, dest + "\\" + Path.GetFileName(dir));
    }
    private void change_Folder(object? sender, EventArgs e)
    {
        Button butt = (Button)sender!;
        start_process($"{label2.Text}\\{butt.Text}\\{butt.Text} 0", $"{label2.Text}\\{butt.Text}\\{label1.Text}");
    }
    private void button_Enter(object? sender, EventArgs e) => start_process($"{label2.Text}\\{textBox1.Text}\\{textBox1.Text} 0", $"{label2.Text}\\{textBox1.Text}\\{label1.Text}");
    private void start_process(string sourse, string dest)
    {
        copy_Folder(sourse, dest);
        foreach (FileInfo file in new DirectoryInfo(sourse).GetFiles("*.dxf")) file.Delete();
        markDXFBase.Convert(new[] { dest });
    }
    private void label1_Click(object? sender, EventArgs e) => label1.Text = Clipboard.GetText().Length > 0 ? Clipboard.GetText() : "Copy and click";
    private void set_Folder(object? sender, EventArgs e)
    {
        FolderBrowserDialog dialog = new();
        dialog.ShowDialog();
        label2.Text = dialog.SelectedPath;
    }
}
