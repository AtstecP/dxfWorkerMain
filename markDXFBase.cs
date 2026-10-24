using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace dxfWorker;

internal static class markDXFBase
{
    public static void Convert(
        string[] args,
        double maximumTextHeight,
        Action<string>? onFileProcessed,
        Func<string, string, bool>? requestLegacyFallback)
    {
        if (args.Length == 0)
            throw new ArgumentException("Не указана папка с файлами DXF.", nameof(args));
        if (maximumTextHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumTextHeight));

        string? folder = args.ElementAtOrDefault(0);
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Папка не существует: {folder}");

        DxfVersion version = DxfVersion.AutoCad2004;
        switch (args.ElementAtOrDefault(1))
        {
            case "2010": version = DxfVersion.AutoCad2010; break;
            case "2013": version = DxfVersion.AutoCad2013; break;
            case "2018": version = DxfVersion.AutoCad2018; break;
            case "2007": version = DxfVersion.AutoCad2007; break;
        }

        string[] files = Directory.GetFiles(folder, "*.dxf");
        if (files.Length == 0)
            throw new InvalidOperationException($"В папке нет DXF-файлов: {folder}");

        string name = Directory.GetParent(files.First())!.Name;
        string outputFolder = Path.Combine(folder, "CypCut_" + name);
        Directory.CreateDirectory(outputFolder);

        foreach (string inputFile in files)
        {
            string stem = Path.GetFileNameWithoutExtension(inputFile);
            string extension = Path.GetExtension(inputFile);
            string outputFile = Path.Combine(outputFolder, $"{stem}_({name}){extension}");
            DxfDocument? document = DxfDocument.Load(inputFile);
            if (document == null)
                throw new InvalidDataException($"Не удалось прочитать DXF: {Path.GetFileName(inputFile)}");

            document.DrawingVariables.AcadVer = version;
            if (!ClosedContourTextPlacer.TryFindBestPlacement(
                    document.Entities.Lines,
                    document.Entities.Arcs,
                    document.Entities.Circles,
                    document.Entities.Polylines2D,
                    document.Entities.Polylines3D,
                    name,
                    maximumTextHeight,
                    out int detectedContours,
                    out ClosedContourTextPlacer.Placement placement))
            {
                string reason = detectedContours == 0
                    ? "замкнутый контур не распознан"
                    : "контур найден, но внутри него не удалось разместить надпись с заданным ограничением размера";
                bool useLegacy = requestLegacyFallback?.Invoke(inputFile, reason) == true;
                if (!useLegacy)
                    throw new OperationCanceledException($"Обработка остановлена для файла {Path.GetFileName(inputFile)}.");
                placement = GetLegacyPlacement(document, name, maximumTextHeight);
            }

            Text label = new(name)
            {
                Layer = new Layer("TextOrder"),
                Color = AciColor.Blue,
                Style = new TextStyle("iso.shx"),
                Height = placement.Height,
                Width = placement.WidthFactor,
                Rotation = placement.Rotation,
                Position = placement.Position
            };
            document.Entities.Add(label);
            document.Save(outputFile);
            onFileProcessed?.Invoke(inputFile);
        }
    }

    private static ClosedContourTextPlacer.Placement GetLegacyPlacement(
        DxfDocument document,
        string name,
        double maximumTextHeight)
    {
        List<double> x = new();
        List<double> y = new();
        foreach (Line line in document.Entities.Lines)
        {
            x.Add(line.StartPoint.X);
            x.Add(line.EndPoint.X);
            y.Add(line.StartPoint.Y);
            y.Add(line.EndPoint.Y);
        }
        foreach (Arc arc in document.Entities.Arcs)
        {
            x.Add(arc.Center.X + arc.Radius);
            x.Add(arc.Center.X - arc.Radius);
            y.Add(arc.Center.Y + arc.Radius);
            y.Add(arc.Center.Y - arc.Radius);
        }
        if (x.Count == 0 || y.Count == 0)
            throw new InvalidOperationException("Старый способ тоже не смог определить границы чертежа.");

        double fontHeight = Math.Min(12.0, maximumTextHeight);
        double drawingWidth = x.Max() - x.Min();
        double drawingHeight = y.Max() - y.Min();
        double rotation = 0.0;
        double widthFactor = 1.0;
        Vector3 position;
        if (drawingHeight > drawingWidth)
        {
            rotation = 90.0;
            widthFactor = fontHeight * 0.4;
            double offsetX = fontHeight * 0.5;
            double offsetY = -drawingHeight * 0.25;
            position = new Vector3(x.Average() + offsetX, y.Average() + offsetY, 0.0);
        }
        else
        {
            double offsetX = -(fontHeight * 0.5 * name.Length / 2.0 + drawingWidth * 0.25);
            double offsetY = -fontHeight / 2.0;
            position = new Vector3(x.Average() + offsetX, y.Average() + offsetY, 0.0);
        }

        return new ClosedContourTextPlacer.Placement(position, fontHeight, rotation, widthFactor);
    }
}


