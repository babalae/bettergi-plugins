namespace BetterGI.Plugin.NcnnModels;

/// <summary>
/// ncnn 模型资源插件的入口占位类型。
/// </summary>
/// <remarks>
/// 本插件不含任何 [PluggableImpl] 实现——它只通过 BgiPluginContent 附带 ncnn 的
/// .param/.bin 模型。主程序按约定从 Plugins/ncnn_models/Models/&lt;类别&gt;/Ncnn/ 读取
/// （见 NcnnModelPaths），与插件系统的可插拔机制无关。此类型仅用于提供一个合法的
/// 入口程序集，使插件清单校验（entry 必须存在）通过。
/// </remarks>
public static class NcnnModelsPlugin
{
}
