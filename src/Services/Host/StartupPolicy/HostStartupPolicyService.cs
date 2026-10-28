using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.Json;
using ExHyperV.Tools;

namespace ExHyperV.Services;

/// <summary>
/// Stores host-wide intent separately from the effective Hyper-V setting. Native
/// Nothing values survive application exit and host restart; no logon task race.
/// </summary>
public static class HostStartupPolicyService
{
    private static readonly string PolicyDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ExHyperV", "HostStartupPolicy");
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HostStartupPolicyController Controller = new(new PolicyStore(), new Platform());
    private static CancellationTokenSource? monitorCancellation;

    public static Task<bool> GetAllowedAsync() => LockedAsync(async () => (await Controller.ReadAsync()).Allowed);

    public static Task SetAllowedAsync(bool allowed) =>
        LockedAsync(async () => { await Controller.SetAllowedAsync(allowed); return true; });

    public static Task<VmStartupPreference?> GetPreferenceAsync(Guid vmId) =>
        LockedAsync(() => Controller.GetPreferenceAsync(vmId));

    public static Task SetPreferenceAsync(VmStartupPreference preference) =>
        LockedAsync(async () => { await Controller.SetPreferenceAsync(preference); return true; });

    public static Task ReconcileAsync() =>
        LockedAsync(async () => { await Controller.ReconcileAsync(); return true; });

    internal static Task ReconcileVmAsync(Guid vmId) =>
        LockedAsync(async () => { await Controller.ReconcileVmAsync(vmId); return true; });

    internal static async Task InitializeAsync()
    {
        lock (Gate)
        {
            if (monitorCancellation == null)
            {
                monitorCancellation = new CancellationTokenSource();
                _ = MonitorAsync(monitorCancellation.Token);
            }
        }
        // Start retries even if the first apply fails (e.g. VMMS is still starting).
        await ReconcileAsync();
    }

    internal static void StopMonitoring()
    {
        lock (Gate)
        {
            monitorCancellation?.Cancel();
            monitorCancellation = null;
        }
    }

    private static async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try { await ReconcileAsync(); }
                catch (Exception ex) { Debug.WriteLine($"[ExHyperV-StartupPolicy] {ex}"); }
            }
        }
        catch (OperationCanceledException) { }
    }

    // A file lock works across all elevated instances and users, without the
    // thread-affinity requirements of a named Mutex around async WMI calls.
    private static async Task<T> LockedAsync<T>(Func<Task<T>> operation)
    {
        await Gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(PolicyDirectory);
            var timeout = Stopwatch.StartNew();
            FileStream? policyLock = null;
            while (policyLock == null)
            {
                try
                {
                    policyLock = new FileStream(Path.Combine(PolicyDirectory, "policy.lock"),
                        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException) when (timeout.Elapsed < TimeSpan.FromSeconds(20)) { await Task.Delay(100); }
            }
            using (policyLock) return await operation();
        }
        finally { Gate.Release(); }
    }

    private sealed class PolicyStore : IHostStartupPolicyStore
    {
        private static string FilePath => Path.Combine(PolicyDirectory, "policy.json");

        public HostStartupPolicyState Load()
        {
            try
            {
                return JsonSerializer.Deserialize<HostStartupPolicyState>(File.ReadAllText(FilePath))
                    ?? throw new InvalidDataException("The host startup policy is empty. Restore policy.json from backup before changing startup settings.");
            }
            catch (FileNotFoundException) { return new(); }
        }

        public void Save(HostStartupPolicyState state)
        {
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, state, new JsonSerializerOptions { WriteIndented = true });
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, FilePath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private sealed class Platform : IVmStartupPolicyPlatform
    {
        private sealed record ReadResult(VmStartupPreference? Preference, string? Error);

        public async Task<IReadOnlyList<VmStartupPreference>> ReadAsync()
        {
            var result = await WmiApi.QueryAsync(
                "SELECT VirtualSystemIdentifier, AutomaticStartupAction, AutomaticStartupActionDelay FROM Msvm_VirtualSystemSettingData " +
                "WHERE VirtualSystemType = 'Microsoft:Hyper-V:System:Realized'",
                obj =>
                {
                    // QueryAsync intentionally skips mapper exceptions for most
                    // inventories. Policy must instead reject the ENTIRE read:
                    // an omitted VM must never lose its recovery preferences.
                    try
                    {
                        string? delay = obj["AutomaticStartupActionDelay"]?.ToString();
                        return new ReadResult(new VmStartupPreference(Guid.Parse(obj["VirtualSystemIdentifier"].ToString()!),
                            Convert.ToUInt16(obj["AutomaticStartupAction"]), string.IsNullOrEmpty(delay) ? 0
                                : checked((int)ManagementDateTimeConverter.ToTimeSpan(delay).TotalSeconds)), null);
                    }
                    catch (Exception ex) { return new ReadResult(null, ex.Message); }
                });
            if (!result.Success) throw new InvalidOperationException(result.Error);
            var errors = result.Data!.Where(row => row.Error != null).Select(row => row.Error).ToArray();
            if (errors.Length > 0) throw new InvalidDataException("Could not read every virtual machine's startup settings: " + string.Join("; ", errors));
            return result.Data!.Select(row => row.Preference!).ToArray();
        }

        public async Task WriteAsync(VmStartupPreference preference)
        {
            var result = await WmiApi.WithObjectAsync(
                $"SELECT * FROM Msvm_VirtualSystemSettingData WHERE VirtualSystemIdentifier = '{preference.VmId}' " +
                "AND VirtualSystemType = 'Microsoft:Hyper-V:System:Realized'",
                obj =>
                {
                    obj["AutomaticStartupAction"] = preference.Action;
                    obj["AutomaticStartupActionDelay"] = ManagementDateTimeConverter.ToDmtfTimeInterval(TimeSpan.FromSeconds(preference.DelaySeconds));
                });
            if (!result.Success) throw new InvalidOperationException(result.Error);
        }
    }
}
