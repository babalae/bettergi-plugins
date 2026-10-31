using BetterGenshinImpact.Core.Plugins;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGI.Plugin.PpOcr;

/// <summary>
/// PP-OCRv5 检测与斯拉夫语言识别模型。实现标识为 ppocr_v5_eslav.model。
/// </summary>
/// <param name="sessions">绑定当前推理运行时的会话工厂，由主程序注入。</param>
/// <param name="plugin">插件上下文，用于定位插件目录中的模型。</param>
[PluggableImpl("model", "PP-OCRv5 斯拉夫语系", Description = "PP-OCRv5 检测与斯拉夫语言识别模型，适用于俄语、乌克兰语、白俄罗斯语。")]
public sealed class PpOcrV5EslavService(BgiOnnxSessionFactory sessions, IPluginContext plugin)
    : PaddleOcrService(sessions, PpOcrModels.Create(plugin, "PP-OCRv5",
        "PpOcrDetV5", @"Det\V5\PP-OCRv5_mobile_det_infer",
        "PpOcrRecV5Eslav", @"Rec\V5\eslav_PP-OCRv5_mobile_rec_infer"));
