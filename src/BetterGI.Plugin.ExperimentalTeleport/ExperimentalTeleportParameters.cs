using System;
using BetterGenshinImpact.GameTask.AutoTrackPath;

namespace BetterGI.Plugin.ExperimentalTeleport;

/// <summary>
/// 一次传送实际使用的参数，在传送开始时由插件设置和主程序“传送操作速度”算出，传送期间不变。
/// </summary>
/// <remarks>
/// PR #3697 把主程序的操作速度改成了倍率配置；插件不修改主程序配置，倍率按
/// TeleportOperationDelayMilliseconds / DefaultTeleportOperationDelayMilliseconds 换算，默认速度下为 1。
/// </remarks>
public sealed class ExperimentalTeleportParameters
{
    /// <summary>
    /// 由设置和主程序传送配置计算参数。
    /// </summary>
    /// <param name="settings">插件设置。</param>
    /// <param name="tpConfig">主程序传送配置。</param>
    public ExperimentalTeleportParameters(ExperimentalTeleportSettings settings, TpConfig tpConfig)
    {
        Multiplier = GetMultiplier(tpConfig);
        var advanced = settings.UseAdvancedParameters;
        DetailedLogs = advanced && settings.DetailedLogs;
        DragDistanceCorrection = advanced && double.IsFinite(settings.DragDistanceCorrection) && settings.DragDistanceCorrection > 0
            ? settings.DragDistanceCorrection
            : ExperimentalTeleportSettings.DefaultDragDistanceCorrection;
        MaxSingleStepDistancePixels = advanced
            ? Positive(settings.MaxSingleStepDistancePixels, ExperimentalTeleportSettings.DefaultMaxSingleStepDistancePixels)
            : ExperimentalTeleportSettings.DefaultMaxSingleStepDistancePixels;
        // 不启用高级参数时，各项按 PR 中的基准值和下限随操作速度缩放
        DragStepIntervalMilliseconds = advanced
            ? Positive(settings.DragStepIntervalMilliseconds, ExperimentalTeleportSettings.DefaultDragStepIntervalMilliseconds)
            : Scale(3d, 1);
        StateRecognitionIntervalMilliseconds = advanced
            ? Positive(settings.StateRecognitionIntervalMilliseconds, ExperimentalTeleportSettings.DefaultStateRecognitionIntervalMilliseconds)
            : Scale(50d, 10);
        StateRecognitionInitialDelayMilliseconds = advanced
            ? Positive(settings.StateRecognitionInitialDelayMilliseconds, ExperimentalTeleportSettings.DefaultStateRecognitionInitialDelayMilliseconds)
            : Scale(100d, 20);
        StateTransitionTimeoutMilliseconds = advanced
            ? Positive(settings.StateTransitionTimeoutMilliseconds, ExperimentalTeleportSettings.DefaultStateTransitionTimeoutMilliseconds)
            : Scale(500d, 250);
        MapOpenTimeoutMilliseconds = advanced
            ? Positive(settings.MapOpenTimeoutMilliseconds, ExperimentalTeleportSettings.DefaultMapOpenTimeoutMilliseconds)
            : Scale(5000d, 2500);
        MapOpenRepressIntervalMilliseconds = advanced
            ? Positive(settings.MapOpenRepressIntervalMilliseconds, ExperimentalTeleportSettings.DefaultMapOpenRepressIntervalMilliseconds)
            : Scale(2000d, 750);
        DragStartDelayMilliseconds = advanced
            ? Positive(settings.DragStartDelayMilliseconds, ExperimentalTeleportSettings.DefaultDragStartDelayMilliseconds)
            : Scale(25d, 15);
        DragReleaseDelayMilliseconds = advanced
            ? Positive(settings.DragReleaseDelayMilliseconds, ExperimentalTeleportSettings.DefaultDragReleaseDelayMilliseconds)
            : Scale(150d, 75);
    }

    /// <summary>
    /// 主程序传送操作速度换算的延时倍率，默认速度下为 1。
    /// </summary>
    public double Multiplier { get; }

    /// <summary>
    /// 是否输出详细调试日志。
    /// </summary>
    public bool DetailedLogs { get; }

    /// <summary>
    /// 理论拖动距离修正倍率。
    /// </summary>
    public double DragDistanceCorrection { get; }

    /// <summary>
    /// 单步最大输入距离，单位：像素。
    /// </summary>
    public int MaxSingleStepDistancePixels { get; }

    /// <summary>
    /// 拖动步进间隔，单位：ms。
    /// </summary>
    public int DragStepIntervalMilliseconds { get; }

    /// <summary>
    /// 状态识别轮询间隔，单位：ms。
    /// </summary>
    public int StateRecognitionIntervalMilliseconds { get; }

    /// <summary>
    /// 状态识别轮询开始前等待，单位：ms。
    /// </summary>
    public int StateRecognitionInitialDelayMilliseconds { get; }

    /// <summary>
    /// 状态切换等待上限，单位：ms。
    /// </summary>
    public int StateTransitionTimeoutMilliseconds { get; }

    /// <summary>
    /// 打开地图总超时，单位：ms。
    /// </summary>
    public int MapOpenTimeoutMilliseconds { get; }

    /// <summary>
    /// 打开地图补按 M 间隔，单位：ms。
    /// </summary>
    public int MapOpenRepressIntervalMilliseconds { get; }

    /// <summary>
    /// 按下左键后开始拖动延迟，单位：ms。
    /// </summary>
    public int DragStartDelayMilliseconds { get; }

    /// <summary>
    /// 拖动结束后松开左键延迟，单位：ms。
    /// </summary>
    public int DragReleaseDelayMilliseconds { get; }

    /// <summary>
    /// 按倍率缩放默认延时，至少 1 ms。
    /// </summary>
    /// <param name="baseMilliseconds">默认速度下的延时。</param>
    public int OperationDelay(int baseMilliseconds)
    {
        var scaled = baseMilliseconds * Multiplier;
        return !double.IsFinite(scaled) || scaled >= int.MaxValue
            ? int.MaxValue
            : Math.Max(1, (int)Math.Round(scaled));
    }

    /// <summary>
    /// 主程序传送操作速度换算的倍率。
    /// </summary>
    private static double GetMultiplier(TpConfig tpConfig)
    {
        var milliseconds = Math.Clamp(
            tpConfig.TeleportOperationDelayMilliseconds,
            TpConfig.MinTeleportOperationDelayMilliseconds,
            TpConfig.MaxTeleportOperationDelayMilliseconds);
        return milliseconds / (double)TpConfig.DefaultTeleportOperationDelayMilliseconds;
    }

    /// <summary>
    /// 非正数时取默认值。
    /// </summary>
    private static int Positive(int value, int defaultValue) => value > 0 ? value : defaultValue;

    /// <summary>
    /// 按倍率缩放基准值并向上取整，不低于下限（与 PR 中 ScaleExperimentalTeleportDelay 相同）。
    /// </summary>
    private int Scale(double baseMilliseconds, int minimumMilliseconds)
    {
        var scaled = Math.Ceiling(baseMilliseconds * Multiplier);
        if (!double.IsFinite(scaled) || scaled >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return Math.Max(minimumMilliseconds, (int)scaled);
    }
}
