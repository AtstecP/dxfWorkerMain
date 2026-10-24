namespace dxfWorker;

public class Form1 : Form
{
    private readonly Label label1 = new()
    {
        AutoSize = true,
        Location = new Point(20, 303),
        Text = "Folder names (separate with semicolons):"
    };
    private readonly Button UN = MakeButton("UN", 58);
    private readonly Button U = MakeButton("U", 98);
    private readonly Button T = MakeButton("T", 138);
    private readonly Button PLas = MakeButton("PL", 175);
    private readonly Button KL = MakeButton("KL", 18);
    private readonly Button set_folder_button = MakeButton("Select folder", 252);
    private readonly Label label2 = new()
    {
        Location = new Point(20, 410),
        Size = new Size(380, 40),
        Text = "Root folder not selected",
        AutoEllipsis = true
    };
    private readonly TextBox textBox1 = new() { Location = new Point(113, 215), Size = new Size(96, 31) };
    private readonly Button buton_Enter = new() { Location = new Point(215, 212), Size = new Size(72, 36), Text = "Enter" };
    private readonly TextBox folderNamesTextBox = new()
    {
        Location = new Point(20, 329),
        Size = new Size(380, 60),
        Multiline = true,
        PlaceholderText = "Order_1; Order_2; Order_3"
    };

    public Form1()
    {
        Text = "DXF Worker";
        ClientSize = new Size(420, 465);
        foreach (Button button in new[] { UN, U, T, PLas, KL }) button.Click += change_Folder;
        set_folder_button.Click += set_Folder;
        buton_Enter.Click += button_Enter;
        Controls.AddRange(new Control[]
        {
            buton_Enter, textBox1, label2, set_folder_button, KL, PLas, T, U, UN,
            label1, folderNamesTextBox
        });
    }

    private static Button MakeButton(string text, int y) => new()
    {
        Location = new Point(113, y),
        Size = new Size(174, 36),
        Text = text,
        UseVisualStyleBackColor = true
    };

    private void copy_Folder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        foreach (string directory in Directory.GetDirectories(source))
            copy_Folder(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private void change_Folder(object? sender, EventArgs e)
    {
        if (sender is Button button)
            start_process(button.Text);
    }

    private void button_Enter(object? sender, EventArgs e) => start_process(textBox1.Text);

    private void start_process(string code)
    {
        string root = label2.Text;
        if (!Directory.Exists(root))
        {
            ShowError("Сначала выберите существующую корневую папку кнопкой Select folder.");
            return;
        }

        code = code.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            ShowError("Введите код детали или выберите его кнопкой.");
            return;
        }

        string[] names = folderNamesTextBox.Text
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length == 0)
        {
            ShowError("Введите хотя бы одно название папки. Несколько названий разделяйте точкой с запятой.");
            return;
        }

        foreach (string name in names)
        {
            if (!IsSimpleFolderName(name))
            {
                ShowError($"Недопустимое название папки: {name}");
                return;
            }
        }

        string codeFolder = Path.Combine(root, code);
        string source = Path.Combine(codeFolder, $"{code} 0");
        if (!Directory.Exists(source))
        {
            ShowError($"Не найдена исходная папка:\n{source}");
            return;
        }

        string[] destinations = names.Select(name => Path.Combine(codeFolder, name)).ToArray();
        if (destinations.Any(Directory.Exists))
        {
            string existing = destinations.First(Directory.Exists);
            ShowError($"Папка уже существует, поэтому обработка остановлена без перезаписи:\n{existing}");
            return;
        }

        try
        {
            // Сначала создаём все копии. Исходные DXF удаляются только после обработки всех названий.
            foreach (string destination in destinations)
                copy_Folder(source, destination);

            foreach (string destination in destinations)
                markDXFBase.Convert(new[] { destination });

            foreach (FileInfo file in new DirectoryInfo(source).GetFiles("*.dxf"))
                file.Delete();

            MessageBox.Show(
                $"Готово. Созданы папки:\n{string.Join("\n", destinations)}",
                "DXF Worker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowError($"Не удалось обработать файлы:\n{exception.Message}");
        }
    }

    private static bool IsSimpleFolderName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return !string.IsNullOrWhiteSpace(name)
            && name != "."
            && name != ".."
            && !Path.IsPathRooted(name)
            && name.IndexOfAny(invalid) < 0
            && !name.Contains(Path.DirectorySeparatorChar)
            && !name.Contains(Path.AltDirectorySeparatorChar);
    }

    private static void ShowError(string message) => MessageBox.Show(
        message, "DXF Worker", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private void set_Folder(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new();
        if (dialog.ShowDialog() == DialogResult.OK)
            label2.Text = dialog.SelectedPath;
    }
}
