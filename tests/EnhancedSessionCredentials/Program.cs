using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ExHyperV.Services;
using ExHyperV.Tools;

// Exercise the real lookup and Windows DPAPI code without opening the user's credential store.
internal static class Program
{
    private static int Main()
    {
        string root = Path.GetFullPath(AppContext.BaseDirectory);
        string temp = Path.Combine(root, "credential-fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string file = Path.Combine(temp, "credentials.json");
            var store = new EnhancedSessionCredentialStore(file);
            string id = "abcd1234-5678-4901-abcd-123456789abc";
            string otherId = "abcd1234-5678-4901-abcd-123456789def";
            string secret = Guid.NewGuid().ToString("N");
            string encrypted = Protect(secret, id);
            object Entry(string name = "Old name", bool enabled = true, string? username = "test-user", string? password = null) =>
                new { VmName = name, Enabled = enabled, Username = username, ProtectedPassword = password ?? encrypted };
            void Save(object? entry) => File.WriteAllText(file, JsonSerializer.Serialize(new Dictionary<string, object?> { [id] = entry }));
            void AssertApplied(string inputId, string displayName, string expectedId, string expectedPassword)
            {
                var settings = new RdpConnectionSettings();
                store.Apply(settings, inputId, displayName);
                Require(settings.GuestVmId == expectedId && settings.GuestUsername == "test-user" &&
                    settings.GuestPassword == expectedPassword, "Correct VM must receive its own decrypted credentials.");
                Require(AutoConnectLog.Messages.All(message => !message.Contains(expectedPassword, StringComparison.Ordinal)),
                    "Logs must not contain passwords.");
            }
            void AssertManual(string inputId, bool configured = false)
            {
                Require(store.IsConfigured(inputId) == configured, "Unexpected configured status.");
                var settings = new RdpConnectionSettings { Server = "unchanged-server" };
                store.Apply(settings, inputId, "Old name");
                Require(settings.GuestVmId == null && settings.GuestUsername == null && settings.GuestPassword == null &&
                    settings.Server == "unchanged-server", "Unavailable credentials must leave manual login intact.");
            }

            Save(Entry());
            byte[] original = File.ReadAllBytes(file);
            Require(store.IsConfigured(id), "The original VM ID must be configured.");
            foreach (string name in new[] { "Old name", "Renamed VM", "Renamed again" })
                AssertApplied(id, name, id, secret);
            Require(original.SequenceEqual(File.ReadAllBytes(file)), "Renaming must not rewrite saved credentials.");
            Pass("same VM ID survives repeated renames without rewriting credentials");

            AssertManual(otherId);
            string otherSecret = Guid.NewGuid().ToString("N");
            File.WriteAllText(file, JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                [id] = Entry(),
                [otherId] = Entry(password: Protect(otherSecret, otherId))
            }));
            AssertApplied(otherId, "Old name", otherId, otherSecret);
            AssertApplied(id, "Old name", id, secret);
            Pass("same names and new VM IDs cannot borrow another VM's credentials");

            Save(Entry());
            foreach (string variant in new[] { id.ToUpperInvariant(), Guid.Parse(id).ToString("B"), Guid.Parse(id).ToString("N") })
            {
                Require(store.IsConfigured(variant), "Valid GUID formats must identify the same VM.");
                AssertApplied(variant, "Renamed VM", id, secret);
            }
            foreach (string invalid in new[] { "Old name", "not-a-guid", "", Guid.Empty.ToString() }) AssertManual(invalid);
            Pass("GUID input normalization and rejection of names or invalid IDs");

            foreach (object? entry in new object?[]
            {
                Entry(enabled: false), Entry(username: " "), Entry(username: null), Entry(password: ""),
                new { VmName = "Old name", Enabled = true, Username = "test-user" }, null
            })
            {
                Save(entry);
                AssertManual(id);
            }
            foreach (string invalidJson in new[] { "{", "null", "[]", "{}" })
            {
                File.WriteAllText(file, invalidJson);
                AssertManual(id);
            }
            File.Delete(file);
            AssertManual(id);
            Pass("disabled, incomplete, missing and malformed stores preserve manual login");

            foreach (string corrupt in new[] { "not base64", Convert.ToBase64String(new byte[] { 1, 2, 3 }), Protect(secret, otherId) })
            {
                Save(Entry(password: corrupt));
                // Configuration detection does not decrypt; Apply must handle decryption failure safely.
                AssertManual(id, configured: true);
            }
            Pass("invalid ciphertext and another VM's DPAPI entropy cannot authenticate");
            Console.WriteLine("PASS all enhanced-session credential regressions");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
        finally
        {
            // Only remove this run's generated directory under the test output folder.
            if (Path.GetDirectoryName(Path.GetFullPath(temp)) == root.TrimEnd(Path.DirectorySeparatorChar))
                Directory.Delete(temp, recursive: true);
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private static void Pass(string name) => Console.WriteLine("PASS " + name);

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description,
        ref Blob entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static string Protect(string password, string vmId)
    {
        byte[] plain = Encoding.UTF8.GetBytes(password);
        byte[] entropyBytes = Encoding.UTF8.GetBytes(Guid.Parse(vmId).ToString("D"));
        var input = new Blob { Length = plain.Length, Data = Marshal.AllocHGlobal(plain.Length) };
        var entropy = new Blob { Length = entropyBytes.Length, Data = Marshal.AllocHGlobal(entropyBytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(plain, 0, input.Data, plain.Length);
            Marshal.Copy(entropyBytes, 0, entropy.Data, entropyBytes.Length);
            if (!CryptProtectData(ref input, null, ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output))
                throw new InvalidOperationException("Cannot create synthetic Windows DPAPI test credentials.");
            byte[] encrypted = new byte[output.Length];
            Marshal.Copy(output.Data, encrypted, 0, encrypted.Length);
            return Convert.ToBase64String(encrypted);
        }
        finally
        {
            Array.Clear(plain);
            for (int i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            Marshal.FreeHGlobal(entropy.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }
}

namespace ExHyperV.Services
{
    // Test-only sink: production log paths are never touched.
    internal static class AutoConnectLog
    {
        internal static readonly List<string> Messages = new();
        internal static void Write(string message) => Messages.Add(message);
    }
}
