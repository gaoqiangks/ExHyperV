using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExHyperV.Interaction;
using ExHyperV.Models;
using ExHyperV.Properties;
using ExHyperV.Services;
using ExHyperV.Tools;

namespace ExHyperV.ViewModels;

public sealed record VmManagementChoice(ushort Value, string Label);

public partial class VmIntegrationServiceItem : ObservableObject
{
    public string ClassName { get; }
    public string Name { get; }
    public bool AppliedEnabled { get; private set; }
    [ObservableProperty] private bool _enabled;

    public VmIntegrationServiceItem(VmIntegrationServiceSetting setting)
    {
        ClassName = setting.ClassName;
        Name = setting.ClassName switch
        {
            "Msvm_ShutdownComponentSettingData" => VmManagementText.ShutdownService,
            "Msvm_TimeSyncComponentSettingData" => VmManagementText.TimeSyncService,
            "Msvm_KvpExchangeComponentSettingData" => VmManagementText.DataExchangeService,
            "Msvm_HeartbeatComponentSettingData" => VmManagementText.HeartbeatService,
            "Msvm_VssComponentSettingData" => VmManagementText.BackupService,
            "Msvm_GuestServiceInterfaceComponentSettingData" => VmManagementText.GuestService,
            _ => setting.Name
        };
        _enabled = AppliedEnabled = setting.Enabled;
    }
    public void Accept() => AppliedEnabled = Enabled;
}

public partial class VirtualMachinesPageViewModel
{
    private Guid? _managementVmId;
    private long _managementLoadGeneration;
    [ObservableProperty] private bool _isLoadingVmManagement;
    [ObservableProperty] private bool _isSavingVmManagement;
    [ObservableProperty] private string _managementError = "";
    [ObservableProperty] private string _managementName = "";
    [ObservableProperty] private ushort _managementStartAction;
    [ObservableProperty] private string _managementStartDelay = "0";
    [ObservableProperty] private ushort _managementStopAction;
    [ObservableProperty] private ushort _managementCheckpointType;
    [ObservableProperty] private bool _managementCheckpointTypeAvailable;
    [ObservableProperty] private bool _managementAutomaticCheckpointsAvailable;
    [ObservableProperty] private bool _managementAutomaticCheckpointsEnabled;
    [ObservableProperty] private string _managementCheckpointDirectory = "";
    [ObservableProperty] private string _managementSmartPagingDirectory = "";

    public ObservableCollection<VmIntegrationServiceItem> ManagementIntegrationServices { get; } = [];
    public IReadOnlyList<VmManagementChoice> ManagementStartActions { get; } = [
        new(2, VmManagementText.StartNone), new(3, VmManagementText.StartIfRunning), new(4, VmManagementText.StartAlways)];
    public IReadOnlyList<VmManagementChoice> ManagementStopActions { get; } = [
        new(3, VmManagementText.StopSave), new(2, VmManagementText.StopTurnOff), new(4, VmManagementText.StopShutdown)];
    public IReadOnlyList<VmManagementChoice> ManagementCheckpointTypes { get; } = [
        new(2, VmManagementText.CheckpointDisabled), new(5, VmManagementText.CheckpointStandard),
        new(3, VmManagementText.CheckpointProduction), new(4, VmManagementText.CheckpointProductionOnly)];

    public bool CanEditVmManagement => !IsLoadingVmManagement && !IsSavingVmManagement
        && SelectedVm != null && _managementVmId == SelectedVm.Id && CurrentViewType == VmDetailViewType.Management;
    public bool CanReloadVmManagement => !IsLoadingVmManagement && !IsSavingVmManagement;
    public bool CanEditManagementCheckpoints => CanEditVmManagement && ManagementCheckpointTypeAvailable;
    public bool CanEditManagementAutomaticCheckpoints => CanEditManagementCheckpoints
        && ManagementAutomaticCheckpointsAvailable && ManagementCheckpointType != 2;
    public bool CanEditManagementStartDelay => CanEditVmManagement && ManagementStartAction != 2;

    private void NotifyManagementAvailability()
    {
        OnPropertyChanged(nameof(CanEditVmManagement));
        OnPropertyChanged(nameof(CanReloadVmManagement));
        OnPropertyChanged(nameof(CanEditManagementCheckpoints));
        OnPropertyChanged(nameof(CanEditManagementAutomaticCheckpoints));
        OnPropertyChanged(nameof(CanEditManagementStartDelay));
    }
    partial void OnIsLoadingVmManagementChanged(bool value) => NotifyManagementAvailability();
    partial void OnIsSavingVmManagementChanged(bool value)
    {
        NotifyManagementAvailability();
        OnPropertyChanged(nameof(IsVmListEnabled));
    }
    partial void OnManagementStartActionChanged(ushort value) => NotifyManagementAvailability();
    partial void OnManagementCheckpointTypeChanged(ushort value) => NotifyManagementAvailability();
    partial void OnManagementCheckpointTypeAvailableChanged(bool value) => NotifyManagementAvailability();
    partial void OnManagementAutomaticCheckpointsAvailableChanged(bool value) => NotifyManagementAvailability();

    [RelayCommand]
    private async Task GoToVmManagementSettingsAsync()
    {
        if (SelectedVm == null || IsSavingVmManagement) return;
        CurrentViewType = VmDetailViewType.Management;
        await LoadVmManagementAsync(SelectedVm);
    }

    private async Task LoadVmManagementAsync(VmInstanceViewModel vm)
    {
        long generation = Interlocked.Increment(ref _managementLoadGeneration);
        _managementVmId = null;
        IsLoadingVmManagement = true;
        ManagementError = "";
        ManagementIntegrationServices.Clear();
        try
        {
            var response = await VmManagementService.GetSettingsAsync(vm.Id);
            if (generation != _managementLoadGeneration || SelectedVm?.Id != vm.Id || CurrentViewType != VmDetailViewType.Management) return;
            if (!response.HasData) { ManagementError = FriendlyError.CleanLines(response.Error); return; }
            var settings = response.Data!;
            using (SuppressApply())
            {
                ManagementName = settings.Name;
                ManagementStartAction = settings.AutomaticStartAction;
                ManagementStartDelay = settings.AutomaticStartDelaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ManagementStopAction = settings.AutomaticStopAction;
                ManagementCheckpointTypeAvailable = settings.CheckpointTypeAvailable;
                ManagementCheckpointType = settings.CheckpointType;
                ManagementAutomaticCheckpointsAvailable = settings.AutomaticCheckpointsAvailable;
                ManagementAutomaticCheckpointsEnabled = settings.AutomaticCheckpointsEnabled;
                ManagementCheckpointDirectory = settings.CheckpointDirectory;
                ManagementSmartPagingDirectory = settings.SmartPagingDirectory;
                foreach (var service in settings.IntegrationServices) ManagementIntegrationServices.Add(new(service));
            }
            _managementVmId = vm.Id;
        }
        catch (Exception ex)
        {
            if (generation == _managementLoadGeneration && SelectedVm?.Id == vm.Id) ManagementError = FriendlyError.CleanLines(ex.Message);
        }
        finally { if (generation == _managementLoadGeneration) IsLoadingVmManagement = false; }
    }

    private async Task<bool> SaveVmManagementAsync(Func<Guid, Task<ApiResponse>> save)
    {
        if (!CanEditVmManagement) return false;
        Guid id = SelectedVm.Id;
        IsSavingVmManagement = true;
        ManagementError = "";
        try
        {
            var response = await save(id);
            if (!response.Success)
            {
                ManagementError = FriendlyError.CleanLines(response.Error);
                ShowError(ManagementError);
                return false;
            }
            ShowSuccess(VmManagementText.Saved);
            return true;
        }
        catch (Exception ex) { ManagementError = FriendlyError.CleanLines(ex.Message); ShowError(ManagementError); return false; }
        finally { IsSavingVmManagement = false; }
    }

    [RelayCommand]
    private async Task ApplyManagementNameAsync()
    {
        string name = ManagementName.Trim();
        if (name.Length is < 1 or > 100 || name.Any(char.IsControl)) { ShowError(VmManagementText.InvalidName); return; }
        var vm = SelectedVm;
        await SaveVmManagementAsync(async id =>
        {
            var result = await VmEditService.RenameVmAsync(id, name);
            if (!result.Success) return ApiResponse.Fail(result.Message);
            lock (_renameLockouts) _renameLockouts[id] = (name, DateTime.Now.AddSeconds(5));
            vm.Name = name;
            ManagementName = name;
            return ApiResponse.Ok();
        });
    }

    [RelayCommand]
    private async Task ApplyManagementStartAsync()
    {
        if (!int.TryParse(ManagementStartDelay, out int delay) || delay < 0) { ShowError(VmManagementText.InvalidStart); return; }
        await SaveVmManagementAsync(id => VmManagementService.SetAutomaticStartAsync(id, ManagementStartAction, delay));
    }

    [RelayCommand]
    private async Task ApplyManagementStopAsync() =>
        await SaveVmManagementAsync(id => VmManagementService.SetAutomaticStopAsync(id, ManagementStopAction));

    [RelayCommand]
    private async Task ApplyManagementCheckpointsAsync()
    {
        bool automatic = ManagementCheckpointType != 2 && ManagementAutomaticCheckpointsEnabled;
        if (await SaveVmManagementAsync(id => VmManagementService.SetCheckpointSettingsAsync(id, ManagementCheckpointType, automatic, ManagementCheckpointDirectory)))
            ManagementAutomaticCheckpointsEnabled = automatic;
    }

    [RelayCommand]
    private async Task ApplyManagementSmartPagingAsync() =>
        await SaveVmManagementAsync(id => VmManagementService.SetSmartPagingDirectoryAsync(id, ManagementSmartPagingDirectory));

    [RelayCommand]
    private async Task ApplyManagementIntegrationAsync()
    {
        var changes = ManagementIntegrationServices.Where(item => item.Enabled != item.AppliedEnabled).ToList();
        await SaveVmManagementAsync(async id =>
        {
            foreach (var item in changes)
            {
                var result = await VmManagementService.SetIntegrationServiceAsync(id, item.ClassName, item.Enabled);
                if (!result.Success) { item.Enabled = item.AppliedEnabled; return ApiResponse.Fail(item.Name + ": " + result.Error); }
                item.Accept();
            }
            return ApiResponse.Ok();
        });
    }

    [RelayCommand]
    private void BrowseManagementCheckpoints()
    {
        if (!CanEditVmManagement) return;
        string? path = Dialogs.PickFolder(VmManagementText.CheckpointDirectory, ManagementCheckpointDirectory);
        if (path != null) ManagementCheckpointDirectory = path;
    }

    [RelayCommand]
    private void BrowseManagementSmartPaging()
    {
        if (!CanEditVmManagement) return;
        string? path = Dialogs.PickFolder(VmManagementText.SmartPaging, ManagementSmartPagingDirectory);
        if (path != null) ManagementSmartPagingDirectory = path;
    }
}
