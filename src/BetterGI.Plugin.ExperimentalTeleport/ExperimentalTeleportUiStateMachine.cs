using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Exceptions;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Runtime;
using OpenCvSharp;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGI.Plugin.ExperimentalTeleport;

/// <summary>
/// 实验传送的页面状态。
/// </summary>
internal enum ExperimentalTeleportUiState
{
    /// <summary>未知界面，等待后再识别一次。</summary>
    Unknown1,

    /// <summary>连续两次未知界面，需要恢复。</summary>
    Unknown2,

    /// <summary>主界面（大世界）。</summary>
    MainWorld,

    /// <summary>大地图主界面。</summary>
    MapMain,

    /// <summary>大地图的区域列表。</summary>
    AreaList,

    /// <summary>同一位置有多个点位时出现的候选列表。</summary>
    TeleportCandidateList,

    /// <summary>传送点详情，有传送按钮。</summary>
    TeleportDetails,

    /// <summary>传送按钮位置出现不可传送的提示。</summary>
    TeleportUnavailable,

    /// <summary>传送加载画面。</summary>
    Loading,
}

/// <summary>
/// 状态机执行的操作。
/// </summary>
internal enum ExperimentalTeleportUiOperation
{
    /// <summary>确保处于大地图主界面。</summary>
    EnsureMapMain,

    /// <summary>打开区域列表。</summary>
    OpenAreaList,

    /// <summary>在区域列表中选择区域。</summary>
    SelectArea,

    /// <summary>确认传送并等待完成。</summary>
    ConfirmTeleport,
}

/// <summary>
/// 一个阶段的运行状态。
/// </summary>
internal sealed class ExperimentalTeleportUiContext
{
    /// <summary>当前操作。</summary>
    public required ExperimentalTeleportUiOperation Operation { get; init; }

    /// <summary>地图名称。</summary>
    public required string MapName { get; init; }

    /// <summary>要选择的区域。</summary>
    public string? AreaName { get; init; }

    /// <summary>目标传送点。</summary>
    public GiTpPosition? TargetTp { get; init; }

    /// <summary>面板未出现时的回退点击。</summary>
    public Action? FallbackClick { get; init; }

    /// <summary>是否已执行回退点击。</summary>
    public bool FallbackUsed { get; set; }

    /// <summary>候选列表已点击次数。</summary>
    public int CandidateClickAttempts { get; set; }

    /// <summary>已按 F 确认的次数。</summary>
    public int ConfirmClickAttempts { get; set; }

    /// <summary>未知界面已恢复的次数。</summary>
    public int UnknownRecoveryAttempts { get; set; }

    /// <summary>是否已开始未知界面计时。</summary>
    public bool UnknownDelayStarted { get; set; }

    /// <summary>首次识别为未知界面的时刻。</summary>
    public long UnknownDetectedAt { get; set; }

    /// <summary>确认后是否识别到加载画面。</summary>
    public bool LoadingObserved { get; set; }

    /// <summary>是否已记录“未识别到加载画面按成功处理”的日志。</summary>
    public bool LoadingFallbackLogged { get; set; }

    /// <summary>按 F 确认的时刻，未确认时为 0。</summary>
    public long ConfirmIssuedAt { get; set; }
}

/// <summary>
/// 实验传送只负责页面状态识别与状态切换；具体选点和国家 OCR 仍由 <see cref="ExperimentalTpTask"/> 中复制的原逻辑执行。
/// 复制自 PR #3697 的 ExperimentalTeleportUiStateMachine，改为运行环境写法，参数改由插件设置提供。
/// </summary>
internal sealed class ExperimentalTeleportUiStateMachine
{
    private const int UnknownRecheckDelayMilliseconds = 100;
    private const int StateTransitionSettleDelayMilliseconds = 50;
    private const int CandidateClickRetryCount = 2;
    private const int ConfirmClickRetryCount = 2;
    private const int UnknownRecoveryRetryCount = 2;
    private const int MaximumStateIterations = 1000;
    private const int TeleportMinimumCompletionMilliseconds = 1000;
    private const int TeleportLoadingFallbackMilliseconds = 5000;
    private const double LoadingSaturationStdThreshold = 25d;
    private const double LoadingExtremeGrayRatioThreshold = 0.85d;
    private const int LoadingDarkGrayUpperBound = 70;
    private const int LoadingLightGrayLowerBound = 220;
    private const int LoadingRoiX1080P = 480;
    private const int LoadingRoiY1080P = 80;
    private const int LoadingRoiWidth1080P = 960;
    private const int LoadingRoiHeight1080P = 540;

    /// <summary>
    /// 所属的传送任务，提供原有的识别与点击能力
    /// </summary>
    private readonly ExperimentalTpTask _host;

    /// <summary>
    /// 运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 实验参数
    /// </summary>
    private readonly ExperimentalTeleportParameters _parameters;

    /// <summary>
    /// 取消令牌
    /// </summary>
    private readonly CancellationToken _cancellationToken;

    /// <summary>
    /// 创建页面状态机。
    /// </summary>
    /// <param name="host">所属的传送任务。</param>
    /// <param name="runtime">运行环境。</param>
    /// <param name="parameters">实验参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public ExperimentalTeleportUiStateMachine(
        ExperimentalTpTask host,
        GameRuntime runtime,
        ExperimentalTeleportParameters parameters,
        CancellationToken cancellationToken)
    {
        _host = host;
        _runtime = runtime;
        _parameters = parameters;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// 确保处于大地图主界面：在主界面时按 M 打开，在其他界面时关闭或恢复。
    /// </summary>
    public Task EnsureMapMainAsync(string mapName)
    {
        return RunPhaseAsync(
            new ExperimentalTeleportUiContext
            {
                Operation = ExperimentalTeleportUiOperation.EnsureMapMain,
                MapName = mapName,
            },
            ExperimentalTeleportUiState.MapMain,
            ExperimentalTeleportUiState.MapMain);
    }

    /// <summary>
    /// 切换区域：确保在大地图主界面，打开区域列表，再选择区域并等待回到大地图主界面。
    /// </summary>
    public async Task SwitchAreaAsync(string areaName, string mapName)
    {
        await EnsureMapMainAsync(mapName);
        var openContext = new ExperimentalTeleportUiContext
        {
            Operation = ExperimentalTeleportUiOperation.OpenAreaList,
            MapName = mapName,
            AreaName = areaName,
        };
        await RunPhaseAsync(openContext, ExperimentalTeleportUiState.AreaList, ExperimentalTeleportUiState.MapMain);
        await RunPhaseAsync(
            new ExperimentalTeleportUiContext
            {
                Operation = ExperimentalTeleportUiOperation.SelectArea,
                MapName = mapName,
                AreaName = areaName,
            },
            ExperimentalTeleportUiState.MapMain,
            ExperimentalTeleportUiState.AreaList);
    }

    /// <summary>
    /// 点击目标后确认传送：处理候选列表、按 F，并等待加载结束回到主界面。
    /// </summary>
    public Task ConfirmTeleportAsync(string mapName, GiTpPosition targetTp, Action? fallbackClick)
    {
        return RunPhaseAsync(
            new ExperimentalTeleportUiContext
            {
                Operation = ExperimentalTeleportUiOperation.ConfirmTeleport,
                MapName = mapName,
                TargetTp = targetTp,
                FallbackClick = fallbackClick,
            },
            ExperimentalTeleportUiState.MainWorld,
            ExperimentalTeleportUiState.TeleportDetails);
    }

    private async Task RunPhaseAsync(
        ExperimentalTeleportUiContext context,
        ExperimentalTeleportUiState targetState,
        ExperimentalTeleportUiState initialExpectedState)
    {
        ResetUnknownDetection(context);
        LogDetailed(
            "========== 实验传送状态循环启动，目标状态：{TargetState}，入口预期：{ExpectedState} ==========",
            targetState,
            initialExpectedState);

        var currentState = await WaitForExpectedStateAsync(context, initialExpectedState, "阶段入口");
        for (var iteration = 0; iteration < MaximumStateIterations; iteration++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (currentState == targetState)
            {
                LogDetailed("========== 实验传送状态循环完成，到达目标状态：{State} ==========", currentState);
                return;
            }

            var previousState = currentState;
            currentState = await HandleStateAsync(context, currentState);
            if (currentState != previousState)
            {
                await Delay(GetOperationDelay(StateTransitionSettleDelayMilliseconds));
            }
        }

        throw new InvalidOperationException(
            $"实验传送状态循环达到最大迭代次数 {MaximumStateIterations}，未到达目标状态 {targetState}");
    }

    private Task<ExperimentalTeleportUiState> HandleStateAsync(
        ExperimentalTeleportUiContext context,
        ExperimentalTeleportUiState state)
    {
        return state switch
        {
            ExperimentalTeleportUiState.Unknown1 => HandleUnknown1Async(context),
            ExperimentalTeleportUiState.Unknown2 => HandleUnknown2Async(context),
            ExperimentalTeleportUiState.MainWorld => HandleMainWorldAsync(context),
            ExperimentalTeleportUiState.MapMain => HandleMapMainAsync(context),
            ExperimentalTeleportUiState.AreaList => HandleAreaListAsync(context),
            ExperimentalTeleportUiState.TeleportCandidateList => HandleTeleportCandidateListAsync(context),
            ExperimentalTeleportUiState.TeleportDetails => HandleTeleportDetailsAsync(context),
            ExperimentalTeleportUiState.TeleportUnavailable => HandleTeleportUnavailableAsync(context),
            ExperimentalTeleportUiState.Loading => HandleLoadingAsync(context),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
    }

    private async Task<ExperimentalTeleportUiState> HandleUnknown1Async(ExperimentalTeleportUiContext context)
    {
        var delay = GetOperationDelay(UnknownRecheckDelayMilliseconds);
        var elapsed = Environment.TickCount64 - context.UnknownDetectedAt;
        if (elapsed < delay)
        {
            await Delay(delay - (int)elapsed);
        }

        return DetectCurrentState(context);
    }

    private async Task<ExperimentalTeleportUiState> HandleUnknown2Async(ExperimentalTeleportUiContext context)
    {
        if (context.Operation == ExperimentalTeleportUiOperation.ConfirmTeleport)
        {
            if (context.ConfirmIssuedAt > 0)
            {
                var fallbackDelay = GetOperationDelay(TeleportLoadingFallbackMilliseconds);
                var elapsed = Environment.TickCount64 - context.ConfirmIssuedAt;
                if (elapsed < fallbackDelay)
                {
                    await Delay(Math.Min(GetStateRecognitionInterval(), fallbackDelay - (int)elapsed));
                    return DetectCurrentState(context);
                }
            }

            throw new TeleportTargetLocalizationException("传送点交互界面连续两次无法识别");
        }

        if (++context.UnknownRecoveryAttempts > UnknownRecoveryRetryCount)
        {
            throw new InvalidOperationException("实验传送界面恢复次数达到上限");
        }

        await new ReturnMainUiTask(_runtime).Start(_cancellationToken);
        return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.MainWorld, "未知界面恢复");
    }

    private async Task<ExperimentalTeleportUiState> HandleMainWorldAsync(ExperimentalTeleportUiContext context)
    {
        await _host.OpenExperimentalBigMapUi(context.MapName);
        return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.MapMain, "打开大地图");
    }

    private async Task<ExperimentalTeleportUiState> HandleMapMainAsync(ExperimentalTeleportUiContext context)
    {
        if (context.Operation == ExperimentalTeleportUiOperation.OpenAreaList)
        {
            _host.OpenExperimentalAreaList();
            return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.AreaList, "打开国家列表");
        }

        if (context.Operation == ExperimentalTeleportUiOperation.ConfirmTeleport)
        {
            if (context.FallbackUsed || context.FallbackClick == null)
            {
                throw new TeleportTargetLocalizationException("点击传送点后未出现可识别的交互面板");
            }

            context.FallbackUsed = true;
            context.FallbackClick();
            return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.TeleportDetails, "重新点击传送点");
        }

        return DetectCurrentState(context);
    }

    private async Task<ExperimentalTeleportUiState> HandleAreaListAsync(ExperimentalTeleportUiContext context)
    {
        if (context.Operation == ExperimentalTeleportUiOperation.SelectArea && context.AreaName is { } areaName)
        {
            if (await _host.TrySelectExperimentalArea(areaName))
            {
                return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.MapMain, "选择国家");
            }

            LogDetailed("实验传送地区未命中，保持国家列表继续重试：{Area}", areaName);
            await Delay(GetStateRecognitionInterval());
            return DetectCurrentState(context);
        }

        InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
        return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.MapMain, "关闭国家列表");
    }

    private async Task<ExperimentalTeleportUiState> HandleTeleportCandidateListAsync(ExperimentalTeleportUiContext context)
    {
        if (context.Operation != ExperimentalTeleportUiOperation.ConfirmTeleport)
        {
            InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.MapMain, "关闭传送点候选列表");
        }

        if (++context.CandidateClickAttempts > CandidateClickRetryCount ||
            !await _host.TryClickExperimentalMapChooseCandidate(context.TargetTp))
        {
            throw new TpPointNotActivate("传送点候选列表中没有可用目标");
        }

        return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.TeleportDetails, "选择传送点候选");
    }

    private async Task<ExperimentalTeleportUiState> HandleTeleportDetailsAsync(ExperimentalTeleportUiContext context)
    {
        if (context.Operation != ExperimentalTeleportUiOperation.ConfirmTeleport)
        {
            InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.MapMain, "关闭传送详情");
        }

        if (++context.ConfirmClickAttempts > ConfirmClickRetryCount)
        {
            throw new TeleportTargetLocalizationException("传送确认操作达到重试上限");
        }

        await _host.PressExperimentalTeleportConfirmKey();
        context.LoadingObserved = false;
        context.LoadingFallbackLogged = false;
        context.ConfirmIssuedAt = Environment.TickCount64;
        return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.Loading, "确认传送");
    }

    private async Task<ExperimentalTeleportUiState> HandleTeleportUnavailableAsync(
        ExperimentalTeleportUiContext context)
    {
        if (context.Operation == ExperimentalTeleportUiOperation.ConfirmTeleport)
        {
            // 由外层 TpWithRetries 负责累计同一次传送中的出现次数。
            throw new ExperimentalTeleportUnavailableException();
        }

        await _host.DismissExperimentalTeleportUnavailablePrompt();
        return ExperimentalTeleportUiState.MapMain;
    }

    private async Task<ExperimentalTeleportUiState> HandleLoadingAsync(ExperimentalTeleportUiContext context)
    {
        return await WaitForExpectedStateAsync(context, ExperimentalTeleportUiState.MainWorld, "等待传送完成");
    }

    private async Task<ExperimentalTeleportUiState> WaitForExpectedStateAsync(
        ExperimentalTeleportUiContext context,
        ExperimentalTeleportUiState expectedState,
        string operation)
    {
        var timeout = GetStateTransitionTimeout();
        var interval = GetStateRecognitionInterval();
        var initialDelay = GetStateRecognitionInitialDelay();
        // 首次识别前等待界面完成渲染；该等待不计入状态切换超时。
        if (initialDelay > 0)
        {
            await Delay(initialDelay);
        }

        // 首轮识别立即执行，后续识别按配置间隔轮询；超时从首轮识别开始计时。
        var stopwatch = Stopwatch.StartNew();
        var detectedState = ExperimentalTeleportUiState.Unknown1;
        var pollCount = 0;

        while (stopwatch.ElapsedMilliseconds < timeout)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            detectedState = DetectCurrentState(context, expectedState);
            pollCount++;
            if (detectedState == expectedState ||
                detectedState == ExperimentalTeleportUiState.TeleportUnavailable)
            {
                LogDetailed(
                    "实验传送达到预期状态：operation={Operation} state={State} elapsed={ElapsedMilliseconds}ms polls={Polls}",
                    operation,
                    expectedState,
                    stopwatch.ElapsedMilliseconds,
                    pollCount);
                return detectedState;
            }

            var remaining = timeout - (int)stopwatch.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                break;
            }

            await Delay(Math.Min(interval, remaining));
        }

        LogDetailed(
            "实验传送等待预期状态超时：operation={Operation} expected={ExpectedState} accepted={DetectedState} timeout={TimeoutMilliseconds}ms",
            operation,
            expectedState,
            detectedState,
            timeout);
        return detectedState;
    }

    private ExperimentalTeleportUiState DetectCurrentState(
        ExperimentalTeleportUiContext context,
        ExperimentalTeleportUiState? expectedState = null)
    {
        using var capture = _runtime.CaptureToRectArea();
        if (_host.IsExperimentalTeleportDetails(capture))
        {
            return AcceptKnownState(context, ExperimentalTeleportUiState.TeleportDetails);
        }

        if (Bv.IsInBigMapUi(capture))
        {
            if (_host.IsExperimentalTeleportUnavailable(capture))
            {
                return AcceptKnownState(context, ExperimentalTeleportUiState.TeleportUnavailable);
            }

            // 候选列表识别包含图标模板匹配和 OCR，仅在确认传送流程中执行，
            // 避免等待地图/区域状态时每轮都扫描候选列表。
            if (context.Operation == ExperimentalTeleportUiOperation.ConfirmTeleport &&
                (expectedState is null ||
                 expectedState == ExperimentalTeleportUiState.TeleportCandidateList ||
                 expectedState == ExperimentalTeleportUiState.TeleportDetails) &&
                _host.HasExperimentalMapChooseCandidate(capture, context.TargetTp))
            {
                return AcceptKnownState(context, ExperimentalTeleportUiState.TeleportCandidateList);
            }

            return AcceptKnownState(
                context,
                _host.HasExperimentalMapMainControls(capture)
                    ? ExperimentalTeleportUiState.MapMain
                    : ExperimentalTeleportUiState.AreaList);
        }

        // 亮度特征在部分场景中也可能呈现近似纯暗画面，必须与主界面识别组合，
        // 只有“亮度符合 Loading 且当前不是主界面”时才记录 Loading 状态。
        var isInMainUi = Bv.IsInMainUi(capture);
        if (context.Operation == ExperimentalTeleportUiOperation.ConfirmTeleport && context.ConfirmIssuedAt > 0)
        {
            // Loading 画面只需要检查固定 ROI；确认传送完成阶段优先执行该检查，
            // 不必先做候选列表或地图状态识别。
            if (!isInMainUi && IsLoadingScreen(capture.SrcMat, _runtime.SystemInfo.ScaleTo1080PRatio))
            {
                context.LoadingObserved = true;
                return AcceptKnownState(context, ExperimentalTeleportUiState.Loading);
            }

            var elapsed = Environment.TickCount64 - context.ConfirmIssuedAt;
            if (isInMainUi)
            {
                if (context.LoadingObserved && elapsed >= GetOperationDelay(TeleportMinimumCompletionMilliseconds))
                {
                    return AcceptKnownState(context, ExperimentalTeleportUiState.MainWorld);
                }

                if (!context.LoadingObserved && elapsed >= GetOperationDelay(TeleportLoadingFallbackMilliseconds))
                {
                    if (!context.LoadingFallbackLogged)
                    {
                        context.LoadingFallbackLogged = true;
                        Logger.LogWarning("实验传送确认后未识别到 Loading，但已等待 {ElapsedMilliseconds}ms 并处于主界面，按传送成功处理", elapsed);
                    }

                    return AcceptKnownState(context, ExperimentalTeleportUiState.MainWorld);
                }
            }
            else
            {
                return context.LoadingObserved
                    ? ExperimentalTeleportUiState.Loading
                    : ResolveUnknownState(context);
            }

            return ResolveUnknownState(context);
        }

        return context.Operation != ExperimentalTeleportUiOperation.ConfirmTeleport && isInMainUi
            ? AcceptKnownState(context, ExperimentalTeleportUiState.MainWorld)
            : ResolveUnknownState(context);
    }

    /// <summary>
    /// 判断是否为传送加载画面：中央区域饱和度很低，且绝大多数像素为纯暗或纯亮。
    /// </summary>
    /// <param name="bgr">截图。</param>
    /// <param name="scale">截图到 1080P 的缩放比例。</param>
    private static bool IsLoadingScreen(Mat bgr, double scale)
    {
        if (bgr.Empty())
        {
            return false;
        }

        var roiX = Math.Clamp((int)Math.Round(LoadingRoiX1080P * scale), 0, bgr.Width);
        var roiY = Math.Clamp((int)Math.Round(LoadingRoiY1080P * scale), 0, bgr.Height);
        var roiWidth = Math.Min(
            (int)Math.Round(LoadingRoiWidth1080P * scale),
            bgr.Width - roiX);
        var roiHeight = Math.Min(
            (int)Math.Round(LoadingRoiHeight1080P * scale),
            bgr.Height - roiY);
        if (roiWidth <= 0 || roiHeight <= 0)
        {
            return false;
        }

        using var roi = new Mat(bgr, new Rect(roiX, roiY, roiWidth, roiHeight));
        using var gray = new Mat();
        using var darkMask = new Mat();
        using var lightMask = new Mat();
        Mat? hsv = null;
        try
        {
            if (roi.Channels() == 1)
            {
                roi.CopyTo(gray);
            }
            else
            {
                hsv = new Mat();
                Cv2.CvtColor(roi, hsv, ColorConversionCodes.BGR2HSV);
                Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
            }

            var saturationStd = 0d;
            if (hsv is not null)
            {
                Cv2.MeanStdDev(hsv, out _, out var stdDev);
                saturationStd = stdDev.Val1;
            }

            var total = gray.Rows * gray.Cols;
            if (total <= 0)
            {
                return false;
            }

            Cv2.Threshold(gray, darkMask, LoadingDarkGrayUpperBound, 255, ThresholdTypes.BinaryInv);
            Cv2.Threshold(gray, lightMask, LoadingLightGrayLowerBound, 255, ThresholdTypes.Binary);
            var darkRatio = (double)Cv2.CountNonZero(darkMask) / total;
            var lightRatio = (double)Cv2.CountNonZero(lightMask) / total;
            return saturationStd < LoadingSaturationStdThreshold &&
                   (darkRatio >= LoadingExtremeGrayRatioThreshold ||
                    lightRatio >= LoadingExtremeGrayRatioThreshold);
        }
        finally
        {
            hsv?.Dispose();
        }
    }

    private static ExperimentalTeleportUiState AcceptKnownState(
        ExperimentalTeleportUiContext context,
        ExperimentalTeleportUiState state)
    {
        ResetUnknownDetection(context);
        context.UnknownRecoveryAttempts = 0;
        return state;
    }

    private ExperimentalTeleportUiState ResolveUnknownState(ExperimentalTeleportUiContext context)
    {
        if (!context.UnknownDelayStarted)
        {
            context.UnknownDelayStarted = true;
            context.UnknownDetectedAt = Environment.TickCount64;
            return ExperimentalTeleportUiState.Unknown1;
        }

        return Environment.TickCount64 - context.UnknownDetectedAt >= GetOperationDelay(UnknownRecheckDelayMilliseconds)
            ? ExperimentalTeleportUiState.Unknown2
            : ExperimentalTeleportUiState.Unknown1;
    }

    /// <summary>
    /// 在运行环境上等待。
    /// </summary>
    private Task Delay(int milliseconds) => _runtime.Delay(Math.Max(0, milliseconds), _cancellationToken);

    private int GetOperationDelay(int baseDelay) => _parameters.OperationDelay(baseDelay);

    private int GetStateRecognitionInterval() => _parameters.StateRecognitionIntervalMilliseconds;

    private int GetStateRecognitionInitialDelay() => _parameters.StateRecognitionInitialDelayMilliseconds;

    private int GetStateTransitionTimeout() => _parameters.StateTransitionTimeoutMilliseconds;

    private void LogDetailed(string message, params object?[] args)
    {
        if (_parameters.DetailedLogs)
        {
#pragma warning disable CA2254 // 调试日志的模板由调用方给出
            Logger.LogDebug(message, args);
#pragma warning restore CA2254
        }
    }

    private static void ResetUnknownDetection(ExperimentalTeleportUiContext context)
    {
        context.UnknownDelayStarted = false;
        context.UnknownDetectedAt = 0;
    }
}
