using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ExHyperV.Tools;

namespace ExHyperV.Services;

// Credentials are scoped to a VM ID and encrypted for the current Windows user.
internal static class EnhancedSessionCredentials
{
    private sealed class Entry
    {
        public string VmName { get; set; } = "";
        public string Username { get; set; } = "";
        public string ProtectedPassword { get; set; } = "";
        public bool Enabled { get; set; }
    }

    private static string FilePath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "ExHyperV", "EnhancedSessionCredentials.json");

    private static Dictionary<string, Entry> Read() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath)) ?? new()
        : new();

    internal static bool IsConfigured(string vmName)
    {
        try { return Read().Values.Any(e => e.Enabled && e.VmName == vmName); }
        catch { return false; }
    }

    internal static void Apply(RdpConnectionSettings settings, string vmId, string vmName)
    {
        try
        {
            string id = Guid.Parse(vmId).ToString("D");
            if (!Read().TryGetValue(id, out var entry) || !entry.Enabled ||
                entry.VmName != vmName || string.IsNullOrWhiteSpace(entry.Username)) return;
            byte[] encrypted = Convert.FromBase64String(entry.ProtectedPassword);
            byte[] plain = Unprotect(encrypted, Encoding.UTF8.GetBytes(id));
            try
            {
                settings.GuestVmId = id;
                settings.GuestUsername = entry.Username;
                settings.GuestPassword = Encoding.UTF8.GetString(plain);
                AutoConnectLog.Write($"Encrypted enhanced-session credentials loaded: {vmName}");
            }
            finally { Array.Clear(plain); }
        }
        catch
        {
            // Leave the normal guest login available if the Windows account or password changes.
            AutoConnectLog.Write($"Enhanced-session credentials unavailable; manual login remains available: {vmName}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description,
        ref Blob entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static byte[] Unprotect(byte[] encrypted, byte[] entropyBytes)
    {
        var input = new Blob { Length = encrypted.Length, Data = Marshal.AllocHGlobal(encrypted.Length) };
        var entropy = new Blob { Length = entropyBytes.Length, Data = Marshal.AllocHGlobal(entropyBytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(encrypted, 0, input.Data, encrypted.Length);
            Marshal.Copy(entropyBytes, 0, entropy.Data, entropyBytes.Length);
            if (!CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output))
                throw new InvalidOperationException("Windows credential decryption failed.");
            var plain = new byte[output.Length];
            Marshal.Copy(output.Data, plain, 0, plain.Length);
            return plain;
        }
        finally
        {
            if (output.Data != IntPtr.Zero)
            {
                for (int i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0);
                LocalFree(output.Data);
            }
            Marshal.FreeHGlobal(input.Data);
            Marshal.FreeHGlobal(entropy.Data);
        }
    }
}
