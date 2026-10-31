using System.ComponentModel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Plugins;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGI.Plugin.ExperimentalTeleport;

/// <summary>
/// 实验传送的设置界面 VM，在插件页点击该插件的“设置”按钮后显示在弹窗中。属性变化后立即保存到插件设置文件，下一次传送时生效。
/// </summary>
[PluginSettingsView(typeof(ExperimentalTeleportSettingsView))]
public sealed partial class ExperimentalTeleportSettingsViewModel : ObservableObject
{
    /// <summary>
    /// 插件上下文，用于读写设置。
    /// </summary>
    private readonly IPluginContext _plugin;

    /// <summary>
    /// 主程序配置，用于按传送操作速度显示自动计算的参数。
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 正在从设置加载属性，期间的属性变化不保存。
    /// </summary>
    private bool _loading;

    /// <summary>
    /// 是否使用高级参数。
    /// </summary>
    [ObservableProperty]
    private bool _useAdvancedParameters;

    /// <summary>
    /// 是否输出详细调试日志。
    /// </summary>
    [ObservableProperty]
    private bool _detailedLogs;

    /// <summary>
    /// 理论拖动距离修正倍率。
    /// </summary>
    [ObservableProperty]
    private double _dragDistanceCorrection;

    /// <summary>
    /// 单步最大输入距离，单位：像素。
    /// </summary>
    [ObservableProperty]
    private int _maxSingleStepDistancePixels;

    /// <summary>
    /// 拖动步进间隔，单位：ms。
    /// </summary>
    [ObservableProperty]
    private int _dragStepIntervalMilliseconds;

    /// <summary>
    /// 状态识别轮询间隔，单位：ms。
    /// </summary>
    [ObservableProperty]
    private int _stateRecognitionIntervalMilliseconds;

    /// <summary>
    /// 状态识别轮询开始前等待，单位：ms。
    /// </summary>
    [ObservableProperty]
    private int _stateRecognitionInitialDelayMilliseconds;

    /// <summary>
    /// 状态切换等待上限，单位：ms。
    /// </summary>
    [ObservableProperty]
    private int _stateTransitionTimeoutMilliseconds;

    /// <summary>
    /// 打开地图总超时，单位：ms。
    /// </summary>
    [ObservableProperty]
    private int _mapOpenTimeoutMilliseconds;

    /// <summary>
    /// 打开地图补按 M 间隔，单位：ms。
    /// </summary>
    [ObservableProperty]
    private int _mapOpenRepressIntervalMilliseconds;

    /// <summary>
    /// 按下左键后开始拖动延迟，单位：ms。
    /// </summary>
    [ObservableProperty]
    private int _dragStartDelayMilliseconds;

    /// <summary>
    /// 拖动结束后松开左键延迟，单位：ms。
    /// </summary>
    [ObservableProperty]
    private int _dragReleaseDelayMilliseconds;

    /// <summary>
    /// 创建设置 VM 并读取已保存的设置。
    /// </summary>
    /// <param name="plugin">插件上下文，由插件服务容器注入。</param>
    /// <param name="config">主程序配置，由主程序服务容器注入。</param>
    public ExperimentalTeleportSettingsViewModel(IPluginContext plugin, AllConfig config)
    {
        _plugin = plugin;
        _config = config;
        Apply(plugin.Settings.Load<ExperimentalTeleportSettings>());
    }

    /// <summary>
    /// 是否按传送操作速度自动计算参数，即未启用高级参数。
    /// </summary>
    public bool UseAutoParameters => !UseAdvancedParameters;

    /// <summary>
    /// 不启用高级参数时，按当前传送操作速度自动计算出的参数。
    /// </summary>
    public string AutoParametersText
    {
        get
        {
            var parameters = new ExperimentalTeleportParameters(new ExperimentalTeleportSettings(), _config.TpConfig);
            return $"当前操作速度倍率 {parameters.Multiplier:0.##}：拖动步进 {parameters.DragStepIntervalMilliseconds} ms，" +
                   $"状态轮询 {parameters.StateRecognitionIntervalMilliseconds} ms，首次识别前等待 {parameters.StateRecognitionInitialDelayMilliseconds} ms，" +
                   $"状态切换上限 {parameters.StateTransitionTimeoutMilliseconds} ms，打开地图超时 {parameters.MapOpenTimeoutMilliseconds} ms，" +
                   $"补按 M 间隔 {parameters.MapOpenRepressIntervalMilliseconds} ms，按下后延迟 {parameters.DragStartDelayMilliseconds} ms，" +
                   $"松开前延迟 {parameters.DragReleaseDelayMilliseconds} ms。";
        }
    }

    /// <summary>
    /// 高级参数恢复为默认值（不改变是否启用高级参数）。
    /// </summary>
    [RelayCommand]
    private void ResetAdvancedParameters()
    {
        Apply(new ExperimentalTeleportSettings
        {
            UseAdvancedParameters = UseAdvancedParameters,
            DetailedLogs = DetailedLogs,
        });
        Save();
    }

    /// <summary>
    /// 属性变化后保存设置；切换高级参数时刷新自动参数说明。
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(AutoParametersText) or nameof(UseAutoParameters))
        {
            return;
        }

        if (e.PropertyName == nameof(UseAdvancedParameters))
        {
            OnPropertyChanged(nameof(UseAutoParameters));
            OnPropertyChanged(nameof(AutoParametersText));
        }

        if (!_loading)
        {
            Save();
        }
    }

    /// <summary>
    /// 用设置对象刷新全部属性，期间不保存。
    /// </summary>
    private void Apply(ExperimentalTeleportSettings settings)
    {
        _loading = true;
        try
        {
            UseAdvancedParameters = settings.UseAdvancedParameters;
            DetailedLogs = settings.DetailedLogs;
            DragDistanceCorrection = settings.DragDistanceCorrection;
            MaxSingleStepDistancePixels = settings.MaxSingleStepDistancePixels;
            DragStepIntervalMilliseconds = settings.DragStepIntervalMilliseconds;
            StateRecognitionIntervalMilliseconds = settings.StateRecognitionIntervalMilliseconds;
            StateRecognitionInitialDelayMilliseconds = settings.StateRecognitionInitialDelayMilliseconds;
            StateTransitionTimeoutMilliseconds = settings.StateTransitionTimeoutMilliseconds;
            MapOpenTimeoutMilliseconds = settings.MapOpenTimeoutMilliseconds;
            MapOpenRepressIntervalMilliseconds = settings.MapOpenRepressIntervalMilliseconds;
            DragStartDelayMilliseconds = settings.DragStartDelayMilliseconds;
            DragReleaseDelayMilliseconds = settings.DragReleaseDelayMilliseconds;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// 保存当前属性到插件设置文件。
    /// </summary>
    private void Save()
    {
        _plugin.Settings.Save(new ExperimentalTeleportSettings
        {
            UseAdvancedParameters = UseAdvancedParameters,
            DetailedLogs = DetailedLogs,
            DragDistanceCorrection = DragDistanceCorrection,
            MaxSingleStepDistancePixels = MaxSingleStepDistancePixels,
            DragStepIntervalMilliseconds = DragStepIntervalMilliseconds,
            StateRecognitionIntervalMilliseconds = StateRecognitionIntervalMilliseconds,
            StateRecognitionInitialDelayMilliseconds = StateRecognitionInitialDelayMilliseconds,
            StateTransitionTimeoutMilliseconds = StateTransitionTimeoutMilliseconds,
            MapOpenTimeoutMilliseconds = MapOpenTimeoutMilliseconds,
            MapOpenRepressIntervalMilliseconds = MapOpenRepressIntervalMilliseconds,
            DragStartDelayMilliseconds = DragStartDelayMilliseconds,
            DragReleaseDelayMilliseconds = DragReleaseDelayMilliseconds,
        });
    }
}
