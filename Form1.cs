namespace dxfWorker;

public class Form1 : Form
{
    private readonly Label label1 = new()
    {
        AutoSize = true,
        Location = new Point(20, 303),
        Text = "Названия папок (через точку с запятой):"
    };
    private readonly Button UN = MakeButton("UN", 58);
    private readonly Button U = MakeButton("U", 98);
    private readonly Button T = MakeButton("T", 138);
    private readonly Button PLas = MakeButton("PL", 175);
    private readonly Button KL = MakeButton("KL", 18);
    private readonly Button set_folder_button = MakeButton("Выбрать папку", 252);
    private readonly Label label2 = new()
    {
        Location = new Point(20, 429),
        Size = new Size(380, 32),
        Text = "Корневая папка не выбрана",
        AutoEllipsis = true
    };
    private readonly TextBox textBox1 = new() { Location = new Point(113, 215), Size = new Size(96, 31) };
    private readonly Button buton_Enter = new() { Location = new Point(215, 212), Size = new Size(72, 36), Text = "Enter" };
    private readonly TextBox folderNamesTextBox = new()
    {
        Location = new Point(20, 329),
        Size = new Size(380, 60),
        Multiline = true,
        PlaceholderText = "Заказ_1; Заказ_2; Заказ_3"
    };
    private readonly CheckBox deleteOriginalsCheckBox = new()
    {
        AutoSize = true,
        Location = new Point(20, 397),
        Text = "Удалить исходные DXF после успешной обработки",
        Checked = false
    };
    private readonly ProgressBar progressBar = new()
    {
        Location = new Point(20, 468),
        Size = new Size(380, 20),
        Minimum = 0,
        Maximum = 100,
        Value = 0
    };
    private readonly Label progressLabel = new()
    {
        AutoSize = false,
        Location = new Point(20, 493),
        Size = new Size(380, 36),
        Text = "Готово к работе"
    };
    private readonly Control[] inputControls;

    public Form1()
    {
        Text = "DXF Worker";
        ClientSize = new Size(420, 535);
        foreach (Button button in new[] { UN, U, T, PLas, KL }) button.Click += change_Folder;
        set_folder_button.Click += set_Folder;
        buton_Enter.Click += button_Enter;
        Controls.AddRange(new Control[]
        {
            buton_Enter, textBox1, label2, set_folder_button, KL, PLas, T, U, UN,
            label1, folderNamesTextBox, deleteOriginalsCheckBox, progressBar, progressLabel
        });
        inputControls = new Control[] { UN, U, T, PLas, KL, set_folder_button, buton_Enter, textBox1, folderNamesTextBox, deleteOriginalsCheckBox };
    }

    private static Button MakeButton(string text, int y) => new()
    {
        Location = new Point(113, y),
        Size = new Size(174, 36),
        Text = text,
        UseVisualStyleBackColor = true
    };

    private static void CopyFolder(string source, string destination, Action<string> onFileCopied)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            string targetFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: false);
            onFileCopied(targetFile);
        }
        foreach (string directory in Directory.GetDirectories(source))
            CopyFolder(directory, Path.Combine(destination, Path.GetFileName(directory)), onFileCopied);
    }

    private void change_Folder(object? sender, EventArgs e)
    {
        if (sender is Button button)
            _ = StartProcessAsync(button.Text);
    }

    private void button_Enter(object? sender, EventArgs e) => _ = StartProcessAsync(textBox1.Text);

    private async Task StartProcessAsync(string code)
    {
        string root = label2.Text;
        if (!Directory.Exists(root))
        {
            ShowError("Сначала выберите существующую корневую папку кнопкой «Выбрать папку».");
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

        int copiedFileCount = CountFiles(source);
        int dxfFileCount = Directory.GetFiles(source, "*.dxf").Length;
        if (dxfFileCount == 0)
        {
            ShowError($"В исходной папке нет DXF-файлов верхнего уровня:\n{source}");
            return;
        }

        string[] destinations = names.Select(name => Path.Combine(codeFolder, name)).ToArray();
        string? existing = destinations.FirstOrDefault(Directory.Exists);
        if (existing != null)
        {
            ShowError($"Папка уже существует, поэтому обработка остановлена без перезаписи:\n{existing}");
            return;
        }

        bool deleteOriginals = deleteOriginalsCheckBox.Checked;
        int totalWork = names.Length * (copiedFileCount + dxfFileCount);
        int completedWork = 0;
        var progress = new Progress<ProgressUpdate>(update =>
        {
            progressBar.Value = Math.Clamp(update.Percent, progressBar.Minimum, progressBar.Maximum);
            progressLabel.Text = update.Message;
        });

        SetBusy(true);
        progressBar.Value = 0;
        progressLabel.Text = "Подготовка...";
        try
        {
            await Task.Run(() =>
            {
                foreach ((string destination, int index) in destinations.Select((path, i) => (path, i + 1)))
                {
                    string name = Path.GetFileName(destination);
                    CopyFolder(source, destination, copiedFile =>
                    {
                        completedWork++;
                        Report($"Копирование {name}: {Path.GetFileName(copiedFile)}");
                    });

                    markDXFBase.Convert(new[] { destination }, inputFile =>
                    {
                        completedWork++;
                        Report($"Обработка {name}: {Path.GetFileName(inputFile)}");
                    });
                    string convertedFolder = Path.Combine(destination, $"CypCut_{name}");
                    int convertedCount = Directory.Exists(convertedFolder)
                        ? Directory.GetFiles(convertedFolder, "*.dxf").Length
                        : 0;
                    if (convertedCount != dxfFileCount)
                        throw new InvalidOperationException($"Не все DXF обработаны в папке {name}: {convertedCount} из {dxfFileCount}.");
                }

                if (deleteOriginals)
                {
                    foreach (FileInfo file in new DirectoryInfo(source).GetFiles("*.dxf"))
                        file.Delete();
                }

                void Report(string message)
                {
                    int percent = totalWork == 0 ? 100 : completedWork * 100 / totalWork;
                    ((IProgress<ProgressUpdate>)progress).Report(new ProgressUpdate(percent, message));
                }
            });

            progressBar.Value = 100;
            progressLabel.Text = $"Готово: обработано вариантов — {names.Length}";
            string deleteMessage = deleteOriginals
                ? "Исходные DXF удалены."
                : "Исходные DXF оставлены без изменений.";
            MessageBox.Show(
                $"Готово. Созданы папки:\n{string.Join("\n", destinations)}\n\n{deleteMessage}",
                "DXF Worker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            progressLabel.Text = "Ошибка обработки. Проверьте результат.";
            ShowError($"Не удалось завершить обработку. Проверьте исходную папку и папки результата.\n\n{exception.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static int CountFiles(string directory) =>
        Directory.GetFiles(directory).Length + Directory.GetDirectories(directory).Sum(CountFiles);

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

    private void SetBusy(bool busy)
    {
        foreach (Control control in inputControls)
            control.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private static void ShowError(string message) => MessageBox.Show(
        message, "DXF Worker", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private void set_Folder(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new();
        if (dialog.ShowDialog() == DialogResult.OK)
            label2.Text = dialog.SelectedPath;
    }

    private sealed record ProgressUpdate(int Percent, string Message);
}



