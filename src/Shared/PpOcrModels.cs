using System.IO;
using BetterGenshinImpact.Core.Plugins;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGI.Plugin.PpOcr;

/// <summary>
/// 组合插件目录中的 PaddleOCR 模型，所有 OCR 插件共用。
/// 模型文件由工程文件从 BetterGI.Assets.Model 包复制到插件目录的 Models\PaddleOCR 下，目录结构与资源包相同。
/// </summary>
internal static class PpOcrModels
{
    /// <summary>
    /// 模型在插件目录中的根目录。
    /// </summary>
    private const string ModelRoot = @"Models\PaddleOCR";

    /// <summary>
    /// 创建 PP-OCR 的版本参数。V4、V5 的参数与主程序内置的 V6 相同，只有名称不同。
    /// </summary>
    /// <param name="name">版本名称，如 PP-OCRv5。</param>
    public static OcrVersionConfig Version(string name) => new(
        name,
        OcrImgMode.BGR,
        false,
        new OcrNormalizeImage(1.0f / 255.0f, [0.485f, 0.456f, 0.406f], [0.229f, 0.224f, 0.225f]),
        new OcrShape(3, 320, 48));

    /// <summary>
    /// 组合检测与识别模型。识别字典读取识别模型目录中的 inference.yml。
    /// 模型名必须沿用 PpOcrDet、PpOcrRec 前缀：硬件加速设置中的 OCR CPU 线程策略按前缀识别 PaddleOCR 模型。
    /// </summary>
    /// <param name="plugin">插件上下文，用于取得插件目录。</param>
    /// <param name="versionName">版本名称，如 PP-OCRv5。</param>
    /// <param name="detectionName">检测模型名，如 PpOcrDetV5。</param>
    /// <param name="detectionDirectory">检测模型目录，相对 Models\PaddleOCR，与工程文件中的 BgiPaddleOcrModel 一致。</param>
    /// <param name="recognitionName">识别模型名，如 PpOcrRecV5。</param>
    /// <param name="recognitionDirectory">识别模型目录，相对 Models\PaddleOCR，与工程文件中的 BgiPaddleOcrModel 一致。</param>
    /// <param name="preheatImage">预热图，相对 Models\PaddleOCR；为 null 时使用主程序自带的预热图。</param>
    public static PaddleOcrService.PaddleOcrModelType Create(
        IPluginContext plugin,
        string versionName,
        string detectionName,
        string detectionDirectory,
        string recognitionName,
        string recognitionDirectory,
        string? preheatImage = null)
    {
        var version = Version(versionName);
        // BgiOnnxModel 的路径参数可以是绝对路径，Global.Absolute 拼接绝对路径时直接返回它
        return PaddleOcrService.PaddleOcrModelType.Create(
            new BgiOnnxModel(detectionName, ModelFile(plugin, detectionDirectory)),
            version,
            new BgiOnnxModel(recognitionName, ModelFile(plugin, recognitionDirectory)),
            version,
            preheatImage is null ? null : Path.Combine(plugin.PluginDirectory, ModelRoot, preheatImage));
    }

    /// <summary>
    /// 模型目录中的 ONNX 文件。
    /// </summary>
    private static string ModelFile(IPluginContext plugin, string directory)
        => Path.Combine(plugin.PluginDirectory, ModelRoot, directory, "slim.onnx");
}
