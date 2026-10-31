using BetterGenshinImpact.Core.Plugins;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGI.Plugin.PpOcr;

/// <summary>
/// PP-OCRv4 检测与中文识别模型。实现标识为 ppocr_v4.model。
/// </summary>
/// <param name="sessions">绑定当前推理运行时的会话工厂，由主程序注入。</param>
/// <param name="plugin">插件上下文，用于定位插件目录中的模型。</param>
[PluggableImpl("model", "PP-OCRv4 中文", Description = "PP-OCRv4 检测与中文识别模型。")]
public sealed class PpOcrV4Service(BgiOnnxSessionFactory sessions, IPluginContext plugin)
    : PaddleOcrService(sessions, PpOcrModels.Create(plugin, "PP-OCRv4",
        "PpOcrDetV4", @"Det\V4\PP-OCRv4_mobile_det_infer",
        "PpOcrRecV4", @"Rec\V4\PP-OCRv4_mobile_rec_infer"));
