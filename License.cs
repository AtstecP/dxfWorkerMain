using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace dxfWorker;

internal enum LicenseStatus { Valid, Missing, Invalid, Expired, WrongMachine, ClockTampered }

internal sealed record LicenseResult(LicenseStatus Status, string Customer = "", DateTime ExpiresUtc = default);

internal static class License
{
    // Public key only: it can verify codes but cannot create them.
    private const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEgxlEAfwEjexqOszbv3PpRoekhaZNeq1zyG3zX7IQ5T351HTt1/QvA9cYhlzHE27crZqgN44uDUsCFaf7Wqu2AA==";

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dxfWorker");
    private static string LicenseFile => Path.Combine(Dir, "license.dat");
    private static string StateFile => Path.Combine(Dir, "state.dat");

    /// <summary>Short PC identifier the customer sends to the vendor (for PC-locked codes).</summary>
    public static string MachineId()
    {
        string guid = "";
        try
        {
            using RegistryKey? key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            guid = key?.GetValue("MachineGuid") as string ?? "";
        }
        catch { }
        if (guid.Length == 0) guid = Environment.MachineName;
        string hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("dxfWorker|" + guid)))[..12];
        return $"{hex[..4]}-{hex[4..8]}-{hex[8..12]}";
    }

    /// <summary>Checks the stored license and the clock. Called at start and periodically.</summary>
    public static LicenseResult Check()
    {
        string? code = null;
        try { if (File.Exists(LicenseFile)) code = File.ReadAllText(LicenseFile).Trim(); } catch { }
        if (string.IsNullOrEmpty(code)) return new LicenseResult(LicenseStatus.Missing);

        LicenseResult result = Verify(code);
        if (result.Status != LicenseStatus.Valid) return result;
        return ClockOk() ? result : result with { Status = LicenseStatus.ClockTampered };
    }

    /// <summary>Validates a code typed by the user and stores it if valid.</summary>
    public static LicenseResult Activate(string code)
    {
        code = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());
        LicenseResult result = Verify(code);
        if (result.Status != LicenseStatus.Valid) return result;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(LicenseFile, code);
        }
        catch (Exception) { return new LicenseResult(LicenseStatus.Invalid); }
        return ClockOk() ? result : result with { Status = LicenseStatus.ClockTampered };
    }

    private static LicenseResult Verify(string code)
    {
        try
        {
            string[] parts = code.Split('.');
            if (parts.Length != 2) return new LicenseResult(LicenseStatus.Invalid);
            byte[] payload = FromB64(parts[0]);
            byte[] sig = FromB64(parts[1]);

            using ECDsa ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
            if (!ec.VerifyData(payload, sig, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return new LicenseResult(LicenseStatus.Invalid);

            string[] f = Encoding.UTF8.GetString(payload).Split('|');
            if (f.Length != 4 || f[0] != "DXFW1" || !long.TryParse(f[2], out long unix))
                return new LicenseResult(LicenseStatus.Invalid);

            DateTime expires = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
            if (f[3] != "*" && !string.Equals(f[3], MachineId(), StringComparison.OrdinalIgnoreCase))
                return new LicenseResult(LicenseStatus.WrongMachine, f[1], expires);
            if (DateTime.UtcNow >= expires)
                return new LicenseResult(LicenseStatus.Expired, f[1], expires);
            return new LicenseResult(LicenseStatus.Valid, f[1], expires);
        }
        catch { return new LicenseResult(LicenseStatus.Invalid); }
    }

    // Remembers the latest time seen (signed with a PC-specific key) so that setting
    // the Windows clock back to extend the license is detected.
    private static bool ClockOk()
    {
        DateTime now = DateTime.UtcNow;
        long last = 0;
        try
        {
            if (File.Exists(StateFile))
            {
                string[] s = File.ReadAllText(StateFile).Split('|');
                if (s.Length != 2 || !long.TryParse(s[0], out last) || s[1] != Mac(s[0])) return false;
            }
        }
        catch { return false; }

        long nowTicks = now.Ticks;
        if (last > nowTicks + TimeSpan.FromMinutes(2).Ticks) return false; // clock moved back
        try
        {
            Directory.CreateDirectory(Dir);
            long max = Math.Max(last, nowTicks);
            File.WriteAllText(StateFile, $"{max}|{Mac(max.ToString())}");
        }
        catch { }
        return true;
    }

    private static string Mac(string data)
    {
        byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes("state|" + MachineId()));
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data)))[..16];
    }

    private static byte[] FromB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    public static string Describe(LicenseResult r) => r.Status switch
    {
        LicenseStatus.Missing => "Введите код активации.",
        LicenseStatus.Invalid => "Неверный код активации.",
        LicenseStatus.Expired => $"Срок лицензии истёк ({r.ExpiresUtc.ToLocalTime():dd.MM.yyyy HH:mm}). Введите новый код.",
        LicenseStatus.WrongMachine => "Этот код выдан для другого компьютера.",
        LicenseStatus.ClockTampered => "Обнаружено изменение системного времени. Восстановите правильное время.",
        _ => ""
    };
}
