namespace BetterGI.Plugin.ExperimentalTeleport;

/// <summary>
/// 实验传送的设置，保存在插件数据目录的 settings.json。字段与 PR #3697 中 TpConfig 的实验传送参数一一对应。
/// 不启用高级参数时，除拖动距离修正和单步距离外，各项延时由主程序“传送操作速度”自动计算，见 <see cref="ExperimentalTeleportParameters"/>。
/// </summary>
public sealed class ExperimentalTeleportSettings
{
    /// <summary>
    /// 默认理论拖动距离修正倍率。
    /// </summary>
    public const double DefaultDragDistanceCorrection = 0.95d;

    /// <summary>
    /// 默认单步最大输入距离，单位：像素。
    /// </summary>
    public const int DefaultMaxSingleStepDistancePixels = 50;

    /// <summary>
    /// 默认拖动步进间隔，单位：ms。
    /// </summary>
    public const int DefaultDragStepIntervalMilliseconds = 3;

    /// <summary>
    /// 默认状态识别轮询间隔，单位：ms。
    /// </summary>
    public const int DefaultStateRecognitionIntervalMilliseconds = 50;

    /// <summary>
    /// 默认状态识别轮询开始前等待，单位：ms。
    /// </summary>
    public const int DefaultStateRecognitionInitialDelayMilliseconds = 100;

    /// <summary>
    /// 默认状态切换等待上限，单位：ms。
    /// </summary>
    public const int DefaultStateTransitionTimeoutMilliseconds = 500;

    /// <summary>
    /// 默认打开地图总超时，单位：ms。
    /// </summary>
    public const int DefaultMapOpenTimeoutMilliseconds = 5000;

    /// <summary>
    /// 默认打开地图补按 M 间隔，单位：ms。
    /// </summary>
    public const int DefaultMapOpenRepressIntervalMilliseconds = 2000;

    /// <summary>
    /// 默认按下左键后开始拖动延迟，单位：ms。
    /// </summary>
    public const int DefaultDragStartDelayMilliseconds = 25;

    /// <summary>
    /// 默认拖动结束后松开左键延迟，单位：ms。
    /// </summary>
    public const int DefaultDragReleaseDelayMilliseconds = 150;

    /// <summary>
    /// 是否使用下面的高级参数；关闭时按传送操作速度自动计算。
    /// </summary>
    public bool UseAdvancedParameters { get; set; }

    /// <summary>
    /// 是否输出详细调试日志，只在启用高级参数时生效。
    /// </summary>
    public bool DetailedLogs { get; set; }

    /// <summary>
    /// 理论拖动距离修正倍率：实际拖动距离 = 理论距离 × 倍率。
    /// </summary>
    public double DragDistanceCorrection { get; set; } = DefaultDragDistanceCorrection;

    /// <summary>
    /// 拖动时单步最大输入距离，单位：像素。
    /// </summary>
    public int MaxSingleStepDistancePixels { get; set; } = DefaultMaxSingleStepDistancePixels;

    /// <summary>
    /// 拖动步进间隔，单位：ms。
    /// </summary>
    public int DragStepIntervalMilliseconds { get; set; } = DefaultDragStepIntervalMilliseconds;

    /// <summary>
    /// 页面状态识别轮询间隔，单位：ms。
    /// </summary>
    public int StateRecognitionIntervalMilliseconds { get; set; } = DefaultStateRecognitionIntervalMilliseconds;

    /// <summary>
    /// 每次等待页面状态时，首次识别前的等待，单位：ms。不计入状态切换超时。
    /// </summary>
    public int StateRecognitionInitialDelayMilliseconds { get; set; } = DefaultStateRecognitionInitialDelayMilliseconds;

    /// <summary>
    /// 页面状态切换等待上限，单位：ms。
    /// </summary>
    public int StateTransitionTimeoutMilliseconds { get; set; } = DefaultStateTransitionTimeoutMilliseconds;

    /// <summary>
    /// 打开大地图的总超时，单位：ms。
    /// </summary>
    public int MapOpenTimeoutMilliseconds { get; set; } = DefaultMapOpenTimeoutMilliseconds;

    /// <summary>
    /// 大地图未出现时补按 M 的间隔，单位：ms。
    /// </summary>
    public int MapOpenRepressIntervalMilliseconds { get; set; } = DefaultMapOpenRepressIntervalMilliseconds;

    /// <summary>
    /// 按下左键后到开始移动的延迟，单位：ms。
    /// </summary>
    public int DragStartDelayMilliseconds { get; set; } = DefaultDragStartDelayMilliseconds;

    /// <summary>
    /// 拖动到终点后到松开左键的延迟，单位：ms。
    /// </summary>
    public int DragReleaseDelayMilliseconds { get; set; } = DefaultDragReleaseDelayMilliseconds;
}
