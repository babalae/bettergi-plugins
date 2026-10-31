using System.Windows.Controls;

namespace BetterGI.Plugin.ExperimentalTeleport;

/// <summary>
/// 实验传送的设置界面，DataContext 为 <see cref="ExperimentalTeleportSettingsViewModel"/>，由插件设置弹窗按 VM 类型匹配后创建。
/// </summary>
public partial class ExperimentalTeleportSettingsView : UserControl
{
    /// <summary>
    /// 创建设置界面。
    /// </summary>
    public ExperimentalTeleportSettingsView() => InitializeComponent();
}
