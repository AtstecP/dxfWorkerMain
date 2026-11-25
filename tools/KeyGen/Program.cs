// License code generator for dxfWorker. NEVER give this tool or private.key to customers.
//
//   dotnet run --project tools/KeyGen -- init
//   dotnet run --project tools/KeyGen -- gen "Customer name" 1y [MACHINE-ID]
//   dotnet run --project tools/KeyGen -- gen "Test" 1m
//
// Duration: <number><unit>, unit: m = minutes, d = days, y = years (365 days).
// MACHINE-ID is optional: if given, the code works only on that PC.
using System.Security.Cryptography;
using System.Text;

string keyFile = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "private.key");
keyFile = Path.GetFullPath(keyFile);

string cmd = args.ElementAtOrDefault(0) ?? "";
if (cmd == "init")
{
    if (File.Exists(keyFile)) { Console.WriteLine("private.key already exists. Not overwriting: " + keyFile); return 1; }
    using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(keyFile, Convert.ToBase64String(k.ExportPkcs8PrivateKey()));
    Console.WriteLine("Private key saved: " + keyFile);
    Console.WriteLine("PUBLIC KEY (put into LicenseKey.cs):");
    Console.WriteLine(Convert.ToBase64String(k.ExportSubjectPublicKeyInfo()));
    return 0;
}
if (cmd == "gen")
{
    string name = (args.ElementAtOrDefault(1) ?? "").Replace("|", " ").Trim();
    string dur = args.ElementAtOrDefault(2) ?? "";
    string machine = (args.ElementAtOrDefault(3) ?? "*").Trim().ToUpperInvariant();
    if (name.Length == 0 || dur.Length < 2 || !double.TryParse(dur[..^1], out double n))
    { Console.WriteLine("Usage: gen \"Customer\" <1m|30d|1y> [MACHINE-ID]"); return 1; }
    DateTime expires = dur[^1] switch
    {
        'm' => DateTime.UtcNow.AddMinutes(n),
        'd' => DateTime.UtcNow.AddDays(n),
        'y' => DateTime.UtcNow.AddDays(365 * n),
        _ => throw new ArgumentException("unit must be m, d or y")
    };
    if (!File.Exists(keyFile)) { Console.WriteLine("No private.key. Run: init"); return 1; }
    using var ec = ECDsa.Create();
    ec.ImportPkcs8PrivateKey(Convert.FromBase64String(File.ReadAllText(keyFile)), out _);
    byte[] payload = Encoding.UTF8.GetBytes($"DXFW1|{name}|{new DateTimeOffset(expires).ToUnixTimeSeconds()}|{machine}");
    byte[] sig = ec.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    Console.WriteLine($"Customer: {name}\nExpires (local): {expires.ToLocalTime():yyyy-MM-dd HH:mm:ss}\nMachine: {machine}\n\nCODE:");
    Console.WriteLine(B64(payload) + "." + B64(sig));
    return 0;
}
Console.WriteLine("Commands: init | gen \"Customer\" <1m|30d|1y> [MACHINE-ID]");
return 1;

static string B64(byte[] d) => Convert.ToBase64String(d).TrimEnd('=').Replace('+', '-').Replace('/', '_');
