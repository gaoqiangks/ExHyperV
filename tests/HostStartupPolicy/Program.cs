using ExHyperV.Services;

internal static class Program
{
    private static readonly Guid First = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly Guid Second = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
    private static readonly Guid Third = Guid.Parse("cccccccc-3333-3333-3333-333333333333");

    private static async Task Main()
    {
        await Run("default allow leaves Hyper-V unchanged", async () =>
        {
            var (controller, store, platform) = Create();
            await controller.ReconcileAsync();
            await controller.SetAllowedAsync(true);
            Require((await controller.ReadAsync()).Allowed && platform.Writes.Count == 0 && store.Saves == 0);
        });
        await Run("disable persists recovery settings before writes, and enable restores them", async () =>
        {
            var (controller, store, platform) = Create();
            var before = platform.Items.Values.ToArray();
            platform.BeforeWrite = _ => Require(!store.State.Allowed && store.State.Preferences.Count == 2);
            await controller.SetAllowedAsync(false);
            Require(platform.Items.Values.All(p => p.Action == 2));
            Require(await controller.GetPreferenceAsync(First) == before.Single(p => p.VmId == First));
            await controller.SetAllowedAsync(false);
            await controller.ReconcileAsync();
            await controller.SetAllowedAsync(true);
            Require(before.All(p => platform.Items[p.VmId] == p));
            Require(store.State.Allowed && store.State.Preferences.Count == 0);
        });
        await Run("blocked preference edits are saved but never enable native auto start", async () =>
        {
            var (controller, store, platform) = Create();
            await controller.SetAllowedAsync(false);
            var requested = new VmStartupPreference(First, 4, 123);
            await controller.SetPreferenceAsync(requested);
            Require(platform.Items[First].Action == 2 && await controller.GetPreferenceAsync(First) == requested);
            await controller.SetAllowedAsync(true);
            Require(platform.Items[First] == requested);
        });
        await Run("new and externally edited VMs are captured and blocked by GUID", async () =>
        {
            var (controller, store, platform) = Create();
            await controller.SetAllowedAsync(false);
            var newVm = new VmStartupPreference(Third, 3, 55);
            var externalEdit = new VmStartupPreference(Second, 4, 42);
            platform.Items[Third] = newVm;
            platform.Items[Second] = externalEdit;
            await controller.ReconcileAsync();
            Require(platform.Items.Values.All(p => p.Action == 2));
            Require(store.State.Preferences[Third] == newVm && store.State.Preferences[Second] == externalEdit);
            await controller.SetAllowedAsync(true);
            Require(platform.Items[Third] == newVm && platform.Items[Second] == externalEdit);
        });
        await Run("targeted reconciliation succeeds despite an unrelated VM write failure", async () =>
        {
            var (controller, store, platform) = Create();
            await controller.SetAllowedAsync(false);
            var originalFirst = store.State.Preferences[First];
            var originalSecond = store.State.Preferences[Second];
            var newVm = new VmStartupPreference(Third, 4, 29);
            platform.Items[Third] = newVm;
            platform.Items[First] = new(First, 3, 16);
            platform.FailWrite = p => p.VmId == First;
            int writesBefore = platform.Writes.Count;
            await controller.ReconcileVmAsync(Third);
            Require(platform.Items[Third].Action == 2 && platform.Items[First].Action == 3);
            Require(platform.Writes.Skip(writesBefore).All(p => p.VmId == Third));
            Require(store.State.Preferences[Third] == newVm
                && store.State.Preferences[First] == originalFirst
                && store.State.Preferences[Second] == originalSecond);
            // The target's recovery preference remains intact through a later
            // failed global restore and its best-effort suppression rollback.
            platform.FailWrite = p => p.VmId == Second && p.Action != 2;
            await MustThrow(() => controller.SetAllowedAsync(true));
            Require(!store.State.Allowed && store.State.Preferences[Third] == newVm
                && platform.Items.Values.All(p => p.Action == 2));
        });
        await Run("targeted reconciliation preserves deleted snapshots and reports target failures", async () =>
        {
            var (controller, store, platform) = Create();
            await controller.SetAllowedAsync(false);
            var oldFirst = store.State.Preferences[First];
            platform.Items.Remove(First);
            var newVm = new VmStartupPreference(Third, 3, 19);
            platform.Items[Third] = newVm;
            platform.IgnoreWrites = true;
            await MustThrow(() => controller.ReconcileVmAsync(Third));
            Require(store.State.Preferences[First] == oldFirst && store.State.Preferences[Third] == newVm);
            await MustThrow(() => controller.ReconcileVmAsync(First));
            platform.IgnoreWrites = false;
            await controller.ReconcileVmAsync(Third);
            Require(platform.Items[Third].Action == 2 && store.State.Preferences[First] == oldFirst);
        });
        await Run("deleted VM preferences are removed without touching another VM", async () =>
        {
            var (controller, store, platform) = Create();
            await controller.SetAllowedAsync(false);
            platform.Items.Remove(First);
            await controller.ReconcileAsync();
            Require(!store.State.Preferences.ContainsKey(First));
            await controller.SetAllowedAsync(true);
            Require(platform.Items.Count == 1 && platform.Items[Second].Action == 3);
        });
        await Run("failed durable save prevents native changes and cannot mutate loaded state", async () =>
        {
            var (controller, store, platform) = Create();
            store.FailSave = _ => true;
            await MustThrow(() => controller.SetAllowedAsync(false));
            Require(store.State.Allowed && store.State.Preferences.Count == 0 && platform.Writes.Count == 0);
        });
        await Run("partial suppression retains recovery snapshot, suppresses other VMs and reports failure", async () =>
        {
            var (controller, store, platform) = Create();
            platform.FailWrite = p => p.VmId == First;
            await MustThrow(() => controller.SetAllowedAsync(false));
            Require(!store.State.Allowed && store.State.Preferences.Count == 2);
            Require(platform.Items[Second].Action == 2 && platform.Items[First].Action == 4);
            platform.FailWrite = null;
            await controller.ReconcileAsync();
            await controller.SetAllowedAsync(true);
            Require(platform.Items[First].Action == 4 && platform.Items[Second].Action == 3);
        });
        await Run("failed native restore rolls every VM back to blocked", async () =>
        {
            var (controller, store, platform) = Create();
            await controller.SetAllowedAsync(false);
            platform.FailWrite = p => p.VmId == Second && p.Action != 2;
            await MustThrow(() => controller.SetAllowedAsync(true));
            Require(!store.State.Allowed && store.State.Preferences.Count == 2 && platform.Items.Values.All(p => p.Action == 2));
            platform.FailWrite = null;
            await controller.SetAllowedAsync(true);
            Require(store.State.Allowed && platform.Items[First].Action == 4);
        });
        await Run("failed final policy save rolls restored native settings back to blocked", async () =>
        {
            var (controller, store, platform) = Create();
            await controller.SetAllowedAsync(false);
            store.FailSave = s => s.Allowed;
            await MustThrow(() => controller.SetAllowedAsync(true));
            Require(!store.State.Allowed && store.State.Preferences.Count == 2 && platform.Items.Values.All(p => p.Action == 2));
        });
        await Run("rollback still suppresses known VMs when the policy store becomes unavailable", async () =>
        {
            var (controller, store, platform) = Create();
            await controller.SetAllowedAsync(false);
            store.FailSave = _ => store.Saves >= 2;
            await MustThrow(() => controller.SetAllowedAsync(true));
            Require(!store.State.Allowed && platform.Items.Values.All(p => p.Action == 2));
        });
        await Run("rollback preserves a VM created during a failed restore", async () =>
        {
            var (controller, store, platform) = Create();
            await controller.SetAllowedAsync(false);
            var newVm = new VmStartupPreference(Third, 4, 27);
            platform.BeforeWrite = p =>
            {
                if (p.Action != 2) platform.Items.TryAdd(Third, newVm);
            };
            platform.FailWrite = p => p.VmId == Second && p.Action != 2;
            await MustThrow(() => controller.SetAllowedAsync(true));
            Require(store.State.Preferences[Third] == newVm && platform.Items.Values.All(p => p.Action == 2));
        });
        await Run("silent native write failures are verified and reported", async () =>
        {
            var (controller, store, platform) = Create();
            platform.IgnoreWrites = true;
            await MustThrow(() => controller.SetAllowedAsync(false));
            Require(!store.State.Allowed && store.State.Preferences.Count == 2);
        });
        await Run("allowed per-VM edits apply normally; missing and invalid VMs are rejected", async () =>
        {
            var (controller, store, platform) = Create();
            var requested = new VmStartupPreference(First, 2, 17);
            await controller.SetPreferenceAsync(requested);
            Require(await controller.GetPreferenceAsync(First) == requested && store.Saves == 0);
            await MustThrow(() => controller.SetPreferenceAsync(new(Third, 3, 0)));
            await MustThrow(() => controller.SetPreferenceAsync(new(First, 1, 0)));
            await MustThrow(() => controller.SetPreferenceAsync(new(First, 4, -1)));
        });
        Console.WriteLine("All host startup policy scenarios passed.");
    }

    private static (HostStartupPolicyController Controller, MemoryStore Store, MemoryPlatform Platform) Create()
    {
        var store = new MemoryStore();
        var platform = new MemoryPlatform();
        platform.Items[First] = new(First, 4, 15);
        platform.Items[Second] = new(Second, 3, 0);
        return (new(store, platform), store, platform);
    }

    private static async Task Run(string name, Func<Task> test)
    {
        await test();
        Console.WriteLine("PASS " + name);
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Scenario assertion failed.");
    }

    private static async Task MustThrow(Func<Task> action)
    {
        try { await action(); }
        catch { return; }
        throw new InvalidOperationException("Expected the operation to fail.");
    }

    private sealed class MemoryStore : IHostStartupPolicyStore
    {
        public HostStartupPolicyState State { get; private set; } = new();
        public int Saves { get; private set; }
        public Func<HostStartupPolicyState, bool>? FailSave { get; set; }
        // Deliberately return the object itself: the production controller must
        // not mutate a loaded object before an atomic Save has succeeded.
        public HostStartupPolicyState Load() => State;
        public void Save(HostStartupPolicyState state)
        {
            if (FailSave?.Invoke(state) == true) throw new IOException("Simulated durable store failure.");
            State = state;
            Saves++;
        }
    }

    private sealed class MemoryPlatform : IVmStartupPolicyPlatform
    {
        public Dictionary<Guid, VmStartupPreference> Items { get; } = [];
        public List<VmStartupPreference> Writes { get; } = [];
        public Action<VmStartupPreference>? BeforeWrite { get; set; }
        public Func<VmStartupPreference, bool>? FailWrite { get; set; }
        public bool IgnoreWrites { get; set; }
        public Task<IReadOnlyList<VmStartupPreference>> ReadAsync() => Task.FromResult<IReadOnlyList<VmStartupPreference>>(Items.Values.ToArray());
        public Task WriteAsync(VmStartupPreference preference)
        {
            BeforeWrite?.Invoke(preference);
            Writes.Add(preference);
            if (FailWrite?.Invoke(preference) == true) throw new IOException("Simulated Hyper-V write failure.");
            if (!IgnoreWrites) Items[preference.VmId] = preference;
            return Task.CompletedTask;
        }
    }
}
