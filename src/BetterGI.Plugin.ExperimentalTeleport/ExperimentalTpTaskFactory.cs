using System.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Plugins;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGI.Plugin.ExperimentalTeleport;

/// <summary>
/// 实验传送，实现标识为 experimental_teleport.state_machine：创建 <see cref="ExperimentalTpTask"/>。
/// 每次创建时读取插件设置，设置界面的修改对之后创建的传送任务生效。
/// </summary>
/// <param name="plugin">插件上下文：设置、素材目录和日志。</param>
[PluggableImpl("state_machine", "实验传送",
    Description = "PR #3697 的实验性传送：页面状态机、安全区拖动、滑块缩放。参数在插件页中本插件的“设置”里调整。")]
public sealed class ExperimentalTpTaskFactory(IPluginContext plugin) : ITpTaskFactory
{
    /// <inheritdoc />
    public ITpTask Create(GameRuntime runtime, AllConfig config, CancellationToken ct)
    {
        var settings = plugin.Settings.Load<ExperimentalTeleportSettings>();
        var parameters = new ExperimentalTeleportParameters(settings, config.TpConfig);
        var captureRect = runtime.SystemInfo.ScaleMax1080PCaptureRect;
        var assets = TeleportAssets.Get(plugin.PluginDirectory, captureRect.Width, captureRect.Height, plugin.Logger);
        return new ExperimentalTpTask(runtime, config, ct, parameters, assets);
    }
}
