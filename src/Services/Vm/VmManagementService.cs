using System.IO;
using System.Management;
using ExHyperV.Models;
using ExHyperV.Properties;
using ExHyperV.Tools;

namespace ExHyperV.Services;

public static class VmManagementService
{
    private static readonly HashSet<string> IntegrationClasses = new(StringComparer.Ordinal)
    {
        "Msvm_ShutdownComponentSettingData", "Msvm_TimeSyncComponentSettingData",
        "Msvm_KvpExchangeComponentSettingData", "Msvm_HeartbeatComponentSettingData",
        "Msvm_VssComponentSettingData", "Msvm_GuestServiceInterfaceComponentSettingData"
    };

    private static string SettingsQuery(Guid vmId) =>
        $"SELECT * FROM Msvm_VirtualSystemSettingData WHERE VirtualSystemIdentifier = '{vmId}' " +
        "AND VirtualSystemType = 'Microsoft:Hyper-V:System:Realized'";

    public static async Task<ApiResponse<VmManagementSettings>> GetSettingsAsync(Guid vmId)
    {
        var settings = await WmiApi.QueryFirstAsync(SettingsQuery(vmId), obj => new VmManagementSettings
        {
            SettingsPath = obj.Path.RelativePath,
            Name = obj.TryGetString("ElementName") ?? "",
            AutomaticStartAction = Convert.ToUInt16(obj["AutomaticStartupAction"]),
            AutomaticStartDelaySeconds = ReadDelay(obj.TryGetString("AutomaticStartupActionDelay")),
            AutomaticStopAction = Convert.ToUInt16(obj["AutomaticShutdownAction"]),
            CheckpointTypeAvailable = obj.HasProperty("UserSnapshotType"),
            CheckpointType = obj.TryGet<ushort>("UserSnapshotType") ?? 2,
            AutomaticCheckpointsAvailable = obj.HasProperty("AutomaticSnapshotsEnabled"),
            AutomaticCheckpointsEnabled = obj.TryGet<bool>("AutomaticSnapshotsEnabled") ?? false,
            CheckpointDirectory = obj.TryGetString("SnapshotDataRoot") ?? "",
            SmartPagingDirectory = obj.TryGetString("SwapFileDataRoot") ?? ""
        });
        if (!settings.HasData) return ApiResponse<VmManagementSettings>.Fail(settings.Success ? VmManagementText.VmNotFound : settings.Error);
        var components = await WmiApi.QueryAsync(
            $"ASSOCIATORS OF {{{settings.Data!.SettingsPath}}} WHERE AssocClass=Msvm_VirtualSystemSettingDataComponent",
            obj => IntegrationClasses.Contains(obj.ClassPath.ClassName)
                ? new VmIntegrationServiceSetting(obj.ClassPath.ClassName, obj["InstanceID"].ToString()!,
                    obj.TryGetString("ElementName") ?? obj.ClassPath.ClassName, Convert.ToUInt16(obj["EnabledState"]) == 2)
                : null);
        if (!components.Success) return ApiResponse<VmManagementSettings>.Fail(components.Error);
        settings.Data.IntegrationServices = components.Data!.OfType<VmIntegrationServiceSetting>().ToList();
        try
        {
            var preference = await HostStartupPolicyService.GetPreferenceAsync(vmId);
            if (preference != null)
            {
                settings.Data.AutomaticStartAction = preference.Action;
                settings.Data.AutomaticStartDelaySeconds = preference.DelaySeconds;
            }
        }
        catch (Exception ex) { return ApiResponse<VmManagementSettings>.Fail(ex.Message); }
        return settings;
    }

    private static int ReadDelay(string? value) => string.IsNullOrEmpty(value) ? 0
        : checked((int)ManagementDateTimeConverter.ToTimeSpan(value).TotalSeconds);

    public static async Task<ApiResponse> SetAutomaticStartAsync(Guid vmId, ushort action, int delaySeconds)
    {
        if (action is not (2 or 3 or 4) || delaySeconds < 0) return ApiResponse.Fail(VmManagementText.InvalidStart);
        try
        {
            await HostStartupPolicyService.SetPreferenceAsync(new(vmId, action, delaySeconds));
            return ApiResponse.Ok();
        }
        catch (Exception ex) { return ApiResponse.Fail(ex.Message); }
    }

    public static async Task<ApiResponse> SetAutomaticStopAsync(Guid vmId, ushort action)
    {
        if (action is not (2 or 3 or 4)) return ApiResponse.Fail(VmManagementText.InvalidStop);
        if (action == 3)
        {
            var settings = await WmiApi.QueryFirstAsync(SettingsQuery(vmId), obj => obj.Path.RelativePath);
            if (!settings.HasData) return ApiResponse.Fail(settings.Success ? VmManagementText.VmNotFound : settings.Error);
            var gpu = await WmiApi.QueryAsync(
                $"ASSOCIATORS OF {{{settings.Data}}} WHERE AssocClass=Msvm_VirtualSystemSettingDataComponent ResultClass=Msvm_GpuPartitionSettingData",
                obj => obj["InstanceID"].ToString()!);
            // Hosts without GPU partition support can still use the normal save-state action.
            if (!gpu.Success && gpu.Code != unchecked((int)0x80041010)) return ApiResponse.Fail(gpu.Error);
            if (gpu.HasData && gpu.Data!.Count > 0) return ApiResponse.Fail(VmManagementText.GpuSaveUnsupported);
        }
        return await WmiApi.WithObjectAsync(SettingsQuery(vmId), obj => obj["AutomaticShutdownAction"] = action);
    }

    public static Task<ApiResponse> SetCheckpointSettingsAsync(Guid vmId, ushort type, bool automatic, string directory)
    {
        if (type is not (2 or 3 or 4 or 5)) return Task.FromResult(ApiResponse.Fail(VmManagementText.InvalidCheckpoint));
        string? error = ValidateDirectory(directory);
        if (error != null) return Task.FromResult(ApiResponse.Fail(error));
        return WmiApi.WithObjectAsync(SettingsQuery(vmId), obj =>
        {
            if (!obj.HasProperty("UserSnapshotType")) throw new NotSupportedException(Resources.Error_VerNotSupport);
            obj["UserSnapshotType"] = type;
            if (obj.HasProperty("AutomaticSnapshotsEnabled")) obj["AutomaticSnapshotsEnabled"] = automatic;
            else if (automatic) throw new NotSupportedException(Resources.Error_VerNotSupport);
            obj["SnapshotDataRoot"] = directory.Trim();
        });
    }

    public static Task<ApiResponse> SetSmartPagingDirectoryAsync(Guid vmId, string directory)
    {
        string? error = ValidateDirectory(directory);
        return error != null ? Task.FromResult(ApiResponse.Fail(error))
            : WmiApi.WithObjectAsync(SettingsQuery(vmId), obj => obj["SwapFileDataRoot"] = directory.Trim());
    }

    private static string? ValidateDirectory(string? directory)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(directory) && Path.IsPathFullyQualified(directory.Trim())
                && directory.IndexOfAny(Path.GetInvalidPathChars()) < 0 && !File.Exists(directory.Trim())
                && Directory.Exists(Path.GetPathRoot(directory.Trim()))
                ? null : VmManagementText.InvalidDirectory;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return VmManagementText.InvalidDirectory; }
    }

    public static async Task<ApiResponse> SetIntegrationServiceAsync(Guid vmId, string className, bool enabled)
    {
        if (!IntegrationClasses.Contains(className)) return ApiResponse.Fail(VmManagementText.InvalidIntegration);
        var current = await GetSettingsAsync(vmId);
        if (!current.HasData) return ApiResponse.Fail(current.Error);
        var component = current.Data!.IntegrationServices.FirstOrDefault(c => c.ClassName == className);
        if (component == null) return ApiResponse.Fail(VmManagementText.InvalidIntegration);
        return await WmiApi.WithObjectAsync(
            $"SELECT * FROM {className} WHERE InstanceID = '{WmiApi.Escape(component.InstanceId)}'",
            obj => obj["EnabledState"] = enabled ? (ushort)2 : (ushort)3,
            submitMethod: "ModifyResourceSettings", submitParamName: "ResourceSettings", wrapInArray: true);
    }
}
