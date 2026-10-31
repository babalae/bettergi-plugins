using BetterGenshinImpact.Core.Plugins;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGI.Plugin.PpOcr;

/// <summary>
/// PP-OCRv5 检测与中文识别模型，也能识别英文和日文。实现标识为 ppocr_v5.model。
/// </summary>
/// <param name="sessions">绑定当前推理运行时的会话工厂，由主程序注入。</param>
/// <param name="plugin">插件上下文，用于定位插件目录中的模型。</param>
[PluggableImpl("model", "PP-OCRv5 中文", Description = "PP-OCRv5 检测与中文识别模型，也能识别英文和日文。")]
public sealed class PpOcrV5Service(BgiOnnxSessionFactory sessions, IPluginContext plugin)
    : PaddleOcrService(sessions, PpOcrModels.Create(plugin, "PP-OCRv5",
        "PpOcrDetV5", @"Det\V5\PP-OCRv5_mobile_det_infer",
        "PpOcrRecV5", @"Rec\V5\PP-OCRv5_mobile_rec_infer"));
