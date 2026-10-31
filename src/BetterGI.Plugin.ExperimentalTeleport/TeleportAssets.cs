using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGI.Plugin.ExperimentalTeleport;

/// <summary>
/// 插件目录中的识别素材：传送不可用提示和区域列表模板。素材为 1920×1080 下的截图，按捕获区域宽度缩放后缓存。
/// 素材缺失或加载失败时对应的识别对象为 null，调用方回退到 OCR 或跳过该识别，不中断传送。
/// </summary>
internal sealed class TeleportAssets
{
    /// <summary>
    /// 按插件目录与捕获区域尺寸缓存。识别对象持有模板 Mat，缓存到进程结束。
    /// </summary>
    private static readonly ConcurrentDictionary<(string Directory, int Width, int Height), TeleportAssets> Cache = new();

    /// <summary>
    /// 区域中文名 → 模板文件。文件名使用拼音：OpenCV 读取中文文件名的模板会失败。
    /// 每个区域还有一个 b 变体（文件名加 b），千星奇域没有 b 变体。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AreaTemplateFiles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["蒙德"] = "mengde",
        ["璃月"] = "liyue",
        ["稻妻"] = "daozhi",
        ["须弥"] = "xumi",
        ["枫丹"] = "fengdan",
        ["纳塔"] = "nata",
        ["挪德卡莱"] = "nuodekalai",
        ["至冬"] = "zhidong",
        ["层岩巨渊"] = "cengyanjuyuan",
        ["渊下宫"] = "yuanxiagong",
        ["旧日之海"] = "jiurizhihai",
        ["远古圣山"] = "yuangushanshan",
        ["空之神殿"] = "kongzhishendian",
        ["霜月"] = "shuangyue",
        ["尘歌壶"] = "chengehu",
        ["千星奇域"] = "qianxingqiyu",
    };

    /// <summary>
    /// 各区域的主模板。
    /// </summary>
    private readonly Dictionary<string, RecognitionObject> _areaTemplates = new(StringComparer.Ordinal);

    /// <summary>
    /// 各区域的 b 变体模板，主模板未命中时再试。
    /// </summary>
    private readonly Dictionary<string, RecognitionObject> _areaTemplatesB = new(StringComparer.Ordinal);

    /// <summary>
    /// 加载素材。
    /// </summary>
    private TeleportAssets(string pluginDirectory, int width, int height, ILogger logger)
    {
        var assetsDirectory = Path.Combine(pluginDirectory, "Assets", "1920x1080");
        var scale = width / 1920d;

        // 传送不可用提示与主程序的传送按钮在同一区域：rect(1440 * s, ch - 120 * s, 100 * s, 120 * s)
        UnavailablePrompt = TryBuild(logger, Path.Combine(assetsDirectory, "TeleportUnavailablePrompt.png"), scale,
            "TeleportUnavailablePrompt",
            new Rect((int)(1440 * scale), (int)(height - 120 * scale), (int)(100 * scale), (int)(120 * scale)),
            use3Channels: false);

        // 区域列表在右侧 1/3，模板不绑定固定格子，在整块区域内找最佳命中，适应区域顺序变化
        var areaRoi = new Rect(width * 2 / 3, 0, width / 3, height);
        foreach (var (areaName, file) in AreaTemplateFiles)
        {
            if (TryBuild(logger, Path.Combine(assetsDirectory, "switch", file + ".png"), scale, areaName, areaRoi, use3Channels: true) is { } primary)
            {
                _areaTemplates[areaName] = primary;
            }

            var bFile = Path.Combine(assetsDirectory, "switch", file + "b.png");
            if (File.Exists(bFile) && TryBuild(logger, bFile, scale, areaName, areaRoi, use3Channels: true) is { } variant)
            {
                _areaTemplatesB[areaName] = variant;
            }
        }
    }

    /// <summary>
    /// 传送不可用提示（点到不可传送的点位时，传送按钮位置出现的提示），素材缺失时为 null。
    /// </summary>
    public RecognitionObject? UnavailablePrompt { get; }

    /// <summary>
    /// 取指定插件目录和捕获区域尺寸的素材。
    /// </summary>
    /// <param name="pluginDirectory">插件目录。</param>
    /// <param name="width">捕获区域宽度（不超过 1080P 的缩放后尺寸）。</param>
    /// <param name="height">捕获区域高度。</param>
    /// <param name="logger">素材加载失败时记录日志。</param>
    public static TeleportAssets Get(string pluginDirectory, int width, int height, ILogger logger)
        => Cache.GetOrAdd((pluginDirectory, width, height), key => new TeleportAssets(key.Directory, key.Width, key.Height, logger));

    /// <summary>
    /// 是否有该区域的模板。
    /// </summary>
    /// <param name="areaName">区域中文名。</param>
    public bool HasAreaTemplate(string areaName) => _areaTemplates.ContainsKey(areaName) || _areaTemplatesB.ContainsKey(areaName);

    /// <summary>
    /// 在区域列表中模板匹配目标区域：先主模板，再 b 变体。返回命中位置（截图坐标），未命中返回 null。
    /// </summary>
    /// <param name="capture">截图。</param>
    /// <param name="areaName">区域中文名。</param>
    public Rect? MatchArea(ImageRegion capture, string areaName)
    {
        return Match(capture, _areaTemplates.GetValueOrDefault(areaName))
               ?? Match(capture, _areaTemplatesB.GetValueOrDefault(areaName));
    }

    /// <summary>
    /// 用一个模板匹配一次，得分达到阈值时返回命中矩形。
    /// </summary>
    private static Rect? Match(ImageRegion capture, RecognitionObject? template)
    {
        if (template is null)
        {
            return null;
        }

        using var hit = capture.Find(template);
        if (hit.IsExist() && (hit.MatchScore ?? 0d) >= template.Threshold)
        {
            return new Rect(hit.X, hit.Y, hit.Width, hit.Height);
        }

        return null;
    }

    /// <summary>
    /// 加载一张素材并构建模板匹配识别对象；失败时记录日志并返回 null。
    /// </summary>
    private static RecognitionObject? TryBuild(ILogger logger, string path, double scale, string name, Rect roi, bool use3Channels)
    {
        Mat? mat = null;
        try
        {
            if (!File.Exists(path))
            {
                logger.LogWarning("实验传送素材缺失：{Path}", path);
                return null;
            }

            using (var stream = File.OpenRead(path))
            {
                mat = Mat.FromStream(stream, ImreadModes.Color);
            }

            // 低于 1080P 时按宽度缩小素材，与主程序加载素材的方式一致
            if (scale < 1d)
            {
                var resized = ResizeHelper.Resize(mat, scale);
                if (!ReferenceEquals(resized, mat))
                {
                    mat.Dispose();
                    mat = resized;
                }
            }

            var recognitionObject = new RecognitionObject
            {
                Name = name,
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = mat,
                RegionOfInterest = roi,
                Use3Channels = use3Channels,
                Threshold = 0.8,
                DrawOnWindow = false,
            }.InitTemplate();
            // Mat 的所有权交给识别对象，由缓存持有
            mat = null;
            return recognitionObject;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OpenCVException or ArgumentException)
        {
            logger.LogWarning(e, "实验传送素材加载失败：{Path}", path);
            return null;
        }
        finally
        {
            mat?.Dispose();
        }
    }
}
