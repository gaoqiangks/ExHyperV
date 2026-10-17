namespace ExHyperV.Models;

public sealed record VmIntegrationServiceSetting(string ClassName, string InstanceId, string Name, bool Enabled);

public sealed class VmManagementSettings
{
    public string SettingsPath { get; init; } = "";
    public string Name { get; init; } = "";
    public ushort AutomaticStartAction { get; init; }
    public int AutomaticStartDelaySeconds { get; init; }
    public ushort AutomaticStopAction { get; init; }
    public bool CheckpointTypeAvailable { get; init; }
    public ushort CheckpointType { get; init; }
    public bool AutomaticCheckpointsAvailable { get; init; }
    public bool AutomaticCheckpointsEnabled { get; init; }
    public string CheckpointDirectory { get; init; } = "";
    public string SmartPagingDirectory { get; init; } = "";
    public IReadOnlyList<VmIntegrationServiceSetting> IntegrationServices { get; internal set; } = [];
}
