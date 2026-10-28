using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ExHyperV.Tools;

namespace ExHyperV.Services;

// Credentials are scoped to a VM ID and encrypted for the current Windows user.
internal static class EnhancedSessionCredentials
{
    private static EnhancedSessionCredentialStore Store => new(Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "ExHyperV", "EnhancedSessionCredentials.json"));

    internal static bool IsConfigured(string vmId) => Store.IsConfigured(vmId);

    internal static void Apply(RdpConnectionSettings settings, string vmId, string vmName) =>
        Store.Apply(settings, vmId, vmName);
}

// An explicit store path keeps credential tests isolated from the user's saved passwords.
internal sealed class EnhancedSessionCredentialStore(string filePath)
{
    private sealed class Entry
    {
        // Retained for compatibility with existing files; names are display metadata only.
        public string VmName { get; set; } = "";
        public string Username { get; set; } = "";
        public string ProtectedPassword { get; set; } = "";
        public bool Enabled { get; set; }
    }

    private Dictionary<string, Entry> Read() => File.Exists(filePath)
        ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(filePath)) ?? new()
        : new();

    private bool TryGetConfiguredEntry(string vmId, out string id, out Entry entry)
    {
        id = "";
        entry = null!;
        if (!Guid.TryParse(vmId, out var guid) || guid == Guid.Empty) return false;
        id = guid.ToString("D");
        if (!Read().TryGetValue(id, out var candidate) || candidate == null || !candidate.Enabled ||
            string.IsNullOrWhiteSpace(candidate.Username) || string.IsNullOrWhiteSpace(candidate.ProtectedPassword))
            return false;
        entry = candidate;
        return true;
    }

    internal bool IsConfigured(string vmId)
    {
        try { return TryGetConfiguredEntry(vmId, out _, out _); }
        catch { return false; }
    }

    internal void Apply(RdpConnectionSettings settings, string vmId, string vmName)
    {
        try
        {
            if (!TryGetConfiguredEntry(vmId, out string id, out var entry)) return;
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
