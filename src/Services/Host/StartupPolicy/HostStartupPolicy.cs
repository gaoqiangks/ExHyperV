namespace ExHyperV.Services;

public sealed record VmStartupPreference(Guid VmId, ushort Action, int DelaySeconds);

public sealed class HostStartupPolicyState
{
    public bool Allowed { get; set; } = true;
    public Dictionary<Guid, VmStartupPreference> Preferences { get; set; } = [];
}

public interface IHostStartupPolicyStore
{
    HostStartupPolicyState Load();
    void Save(HostStartupPolicyState state);
}

public interface IVmStartupPolicyPlatform
{
    Task<IReadOnlyList<VmStartupPreference>> ReadAsync();
    Task WriteAsync(VmStartupPreference preference);
}

/// <summary>
/// Preserves the requested settings separately from Hyper-V's effective settings.
/// The caller must serialize the entire operation, including across processes.
/// Store.Save must replace its durable state atomically or leave it unchanged.
/// </summary>
public sealed class HostStartupPolicyController(IHostStartupPolicyStore store, IVmStartupPolicyPlatform platform)
{
    private const ushort NoAutomaticStart = 2;

    public Task<HostStartupPolicyState> ReadAsync() => Task.FromResult(Load());

    public async Task<VmStartupPreference?> GetPreferenceAsync(Guid id)
    {
        var state = Load();
        if (!state.Allowed && state.Preferences.TryGetValue(id, out var saved)) return saved;
        return (await ReadPlatformAsync()).GetValueOrDefault(id);
    }

    public async Task SetAllowedAsync(bool allowed)
    {
        var state = Load();
        if (allowed && state.Allowed) return;

        var current = await ReadPlatformAsync();
        CapturePreferences(state, current);
        // Persist every recovery value before changing Hyper-V. A failed save must
        // never leave us with native settings that cannot subsequently be restored.
        state.Allowed = false;
        Save(state);

        if (!allowed)
        {
            await SuppressAndVerifyAsync(current);
            return;
        }

        try
        {
            foreach (var id in current.Keys)
                await platform.WriteAsync(state.Preferences[id]);
            var restored = await ReadPlatformAsync();
            foreach (var (id, actual) in restored)
            {
                if (state.Preferences.TryGetValue(id, out var expected) && actual != expected)
                    throw new InvalidOperationException($"Hyper-V did not restore automatic-start settings for {id}.");
                // A VM created during the operation has no saved override; its
                // native preference remains the authoritative setting on release.
            }
            state.Allowed = true;
            state.Preferences.Clear();
            Save(state);
        }
        catch (Exception restoreError)
        {
            // The durable state remains blocked until every restore and the final
            // atomic save succeed. Re-suppress partial restores before reporting.
            try
            {
                // Reconcile from the still-blocked durable snapshot, capturing
                // any VM created during the failed restore before suppressing it.
                await ReconcileAsync();
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException("Could not restore automatic-start settings; some virtual machines could not be blocked again.",
                    restoreError, rollbackError);
            }
            throw new InvalidOperationException("Could not restore automatic-start settings. The global block remains in effect.", restoreError);
        }
    }

    public async Task ReconcileAsync()
    {
        var state = Load();
        if (state.Allowed) return;
        var current = await ReadPlatformAsync();
        var previousPreferences = new Dictionary<Guid, VmStartupPreference>(state.Preferences);
        CapturePreferences(state, current);
        if (state.Preferences.Count != previousPreferences.Count
            || state.Preferences.Any(item => !previousPreferences.TryGetValue(item.Key, out var previous) || previous != item.Value))
            Save(state);
        await SuppressAndVerifyAsync(current);
    }

    public async Task ReconcileVmAsync(Guid id)
    {
        var state = Load();
        if (state.Allowed) return;
        var current = await ReadPlatformAsync();
        if (!current.TryGetValue(id, out var preference))
            throw new InvalidOperationException($"Virtual machine {id} no longer exists.");

        // Creation/import owns just this VM. An unrelated VM that cannot be
        // modified must not turn an otherwise successful operation into failure.
        // Do not prune other recovery records from this targeted path.
        if (!state.Preferences.TryGetValue(id, out var saved)
            || (preference.Action != NoAutomaticStart && saved != preference))
        {
            state.Preferences[id] = preference;
            Save(state);
        }

        var expected = preference with { Action = NoAutomaticStart };
        if (preference.Action != NoAutomaticStart) await platform.WriteAsync(expected);
        var verified = await ReadPlatformAsync();
        if (!verified.TryGetValue(id, out var actual) || actual != expected)
            throw new InvalidOperationException($"Hyper-V did not block automatic start for {id}.");
    }

    public async Task SetPreferenceAsync(VmStartupPreference preference)
    {
        Validate(preference);
        var state = Load();
        var current = await ReadPlatformAsync();
        if (!current.ContainsKey(preference.VmId))
            throw new InvalidOperationException($"Virtual machine {preference.VmId} no longer exists.");

        if (state.Allowed)
        {
            await platform.WriteAsync(preference);
            var actual = await ReadPlatformAsync();
            if (!actual.TryGetValue(preference.VmId, out var updated) || updated != preference)
                throw new InvalidOperationException($"Hyper-V did not save automatic-start settings for {preference.VmId}.");
            return;
        }

        CapturePreferences(state, current);
        state.Preferences[preference.VmId] = preference;
        Save(state);
        // Suppress all VMs here as well, so an independently changed VM is not
        // accidentally left enabled when saving another VM's preference.
        current[preference.VmId] = preference;
        await SuppressAndVerifyAsync(current);
    }

    private async Task SuppressAndVerifyAsync(Dictionary<Guid, VmStartupPreference> current)
    {
        var failures = new List<Exception>();
        foreach (var preference in current.Values)
        {
            if (preference.Action == NoAutomaticStart) continue;
            try { await platform.WriteAsync(preference with { Action = NoAutomaticStart }); }
            catch (Exception ex) { failures.Add(new InvalidOperationException($"Could not block automatic start for {preference.VmId}.", ex)); }
        }

        try
        {
            foreach (var actual in (await ReadPlatformAsync()).Values)
            {
                if (actual.Action != NoAutomaticStart)
                    failures.Add(new InvalidOperationException($"Automatic start is still enabled for {actual.VmId}; retry applying the global setting."));
            }
        }
        catch (Exception ex) { failures.Add(ex); }

        if (failures.Count != 0)
            throw new AggregateException("The global automatic-start block was saved, but could not be applied to every virtual machine.", failures);
    }

    private static void CapturePreferences(HostStartupPolicyState state, Dictionary<Guid, VmStartupPreference> current)
    {
        foreach (var preference in current.Values)
        {
            // None on a known VM is our own effective override. Never replace its
            // saved requested action with that override on subsequent refreshes.
            if (state.Allowed || !state.Preferences.ContainsKey(preference.VmId) || preference.Action != NoAutomaticStart)
                state.Preferences[preference.VmId] = preference;
        }
        foreach (var id in state.Preferences.Keys.Where(id => !current.ContainsKey(id)).ToArray())
            state.Preferences.Remove(id);
    }

    private HostStartupPolicyState Load()
    {
        var state = store.Load() ?? throw new InvalidOperationException("The host startup policy could not be read.");
        if (state.Preferences == null) throw new InvalidOperationException("The host startup policy is damaged.");
        foreach (var (id, preference) in state.Preferences)
        {
            Validate(preference);
            if (id != preference.VmId) throw new InvalidOperationException("A stored virtual-machine identity does not match its policy key.");
        }
        return Clone(state);
    }

    private void Save(HostStartupPolicyState state) => store.Save(Clone(state));

    private static HostStartupPolicyState Clone(HostStartupPolicyState state) => new()
    {
        Allowed = state.Allowed,
        Preferences = new Dictionary<Guid, VmStartupPreference>(state.Preferences)
    };

    private async Task<Dictionary<Guid, VmStartupPreference>> ReadPlatformAsync()
    {
        var result = new Dictionary<Guid, VmStartupPreference>();
        foreach (var preference in await platform.ReadAsync())
        {
            Validate(preference);
            if (!result.TryAdd(preference.VmId, preference))
                throw new InvalidOperationException($"Hyper-V returned more than one active configuration for {preference.VmId}.");
        }
        return result;
    }

    private static void Validate(VmStartupPreference preference)
    {
        if (preference == null || preference.VmId == Guid.Empty || preference.Action is not (2 or 3 or 4) || preference.DelaySeconds < 0)
            throw new ArgumentException("Invalid virtual-machine automatic-start settings.");
    }
}
