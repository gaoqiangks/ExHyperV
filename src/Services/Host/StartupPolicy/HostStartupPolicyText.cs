using System.Globalization;

namespace ExHyperV.Services;

public static class HostStartupPolicyText
{
    private static bool Chinese => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
    public static string Title => Chinese ? "允许虚拟机随宿主机开机启动" : "Allow virtual machines to start with the host";
    public static string Hint => Chinese
        ? "关闭后禁止所有虚拟机随宿主机启动；重新开启后按各自的设置启动。仍可手动启动虚拟机。"
        : "Turn off to block host-boot startup for all VMs. Turn on to restore each VM's settings. Manual startup remains available.";
    public static string ExternalChanges => Chinese
        ? "如果在 ExHyperV 关闭期间通过其他工具修改自动启动设置，或新增虚拟机，请打开 ExHyperV 重新应用此策略。"
        : "If another tool changes automatic startup or adds a VM while ExHyperV is closed, open ExHyperV to reapply this policy.";
    public static string OverrideHint => Chinese
        ? "此处保存每台虚拟机的启动偏好。全局“允许虚拟机随宿主机开机启动”关闭时，不会自动启动；重新开启后这些设置才生效。"
        : "These are this VM's saved startup preferences. They only take effect when the global Allow virtual machines to start with the host option is on.";
    public static string Loading => Chinese ? "正在读取全局启动设置…" : "Loading global startup settings…";
    public static string Applying => Chinese ? "正在应用到全部虚拟机…" : "Applying to all virtual machines…";
    public static string Allowed => Chinese ? "已允许：按每台虚拟机各自的自动启动设置执行。" : "Allowed: each VM follows its own automatic startup settings.";
    public static string Blocked => Chinese ? "已关闭：全部虚拟机禁止随宿主机启动。" : "Blocked: virtual machines will not start with the host.";
    public static string LoadFailed => Chinese ? "读取失败。请点击“重新应用”重试。" : "Could not load the policy. Choose Reapply to retry.";
    public static string ApplyFailed => Chinese ? "未能完整应用，部分虚拟机可能尚未受此设置控制。请点击“重新应用”重试。" : "The policy was not fully applied; some VMs may not be controlled yet. Choose Reapply to retry.";
    public static string Reapply => Chinese ? "重新应用" : "Reapply";
    public static string ImportWarning => Chinese
        ? "导入已完成，但未能应用全局启动策略。请在设置中重新应用，确认成功后再重启宿主机。"
        : "Import completed, but the global startup policy could not be applied. Reapply it in Settings before restarting the host.";
}
