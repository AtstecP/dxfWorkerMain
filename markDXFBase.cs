using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace dxfWorker;

internal static class markDXFBase
{
    public static void Convert(string[] args, Action<string>? onFileProcessed = null)
    {
        if (args.Length == 0)
            throw new ArgumentException("Не указана папка с файлами DXF.", nameof(args));

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
            List<double> x = new();
            List<double> y = new();
            DxfDocument? document = DxfDocument.Load(inputFile);
            if (document == null)
            {
                onFileProcessed?.Invoke(inputFile);
                continue;
            }

            document.DrawingVariables.AcadVer = version;
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
            {
                onFileProcessed?.Invoke(inputFile);
                continue;
            }

            Text label = new(name)
            {
                Layer = new Layer("TextOrder"),
                Color = AciColor.Blue,
                Style = new TextStyle("iso.shx")
            };
            double width = x.Max() - x.Min();
            double height = y.Max() - y.Min();
            if (height > width)
            {
                label.Rotation = 90.0;
                label.Height = 12.0;
                label.Width = label.Height * 0.4;
                double offsetX = label.Height * 0.5;
                double offsetY = -height * 0.25;
                label.Position = new Vector3(x.Average() + offsetX, y.Average() + offsetY, 0.0);
            }
            else
            {
                label.Height = 12.0;
                double offsetX = -(label.Height * 0.5 * name.Length / 2.0 + width * 0.25);
                double offsetY = -label.Height / 2.0;
                label.Position = new Vector3(x.Average() + offsetX, y.Average() + offsetY, 0.0);
            }
            label.Rotation = height > width ? 90.0 : 0.0;
            document.Entities.Add(label);
            document.Save(outputFile);
            onFileProcessed?.Invoke(inputFile);
        }
    }
}
