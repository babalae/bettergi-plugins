using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Vanara.PInvoke;

namespace BetterGI.Plugin.ExperimentalTeleport;

/// <summary>
/// 实验传送的地图拖动与缩放滑块操作，移植自 PR #3697 的 ExperimentalTeleportDrag。
/// </summary>
/// <remarks>
/// 拖动在避开地图界面按钮（危险区）的安全区内选择起点，沿目标方向直线分步拖动，并回读光标的实际位移。
/// 坐标以游戏捕获区域按不超过 1080P 缩放后的尺寸为准；危险区按 1920×1080 定义后缩放。
/// </remarks>
internal sealed class MapDragger
{
    /// <summary>
    /// 起点与屏幕边缘、危险区之间保留的距离（1080P 像素）。
    /// </summary>
    private const double SafeMargin = 50d;

    /// <summary>
    /// 提前结束拖动时，目标与边缘、危险区之间保留的距离（1080P 像素）。
    /// </summary>
    public const double EarlyStopMargin = 40d;

    /// <summary>
    /// 缩放滑块的 x 坐标（1080P 像素），实际拖动位置再向右偏 10 像素。
    /// </summary>
    private const double ZoomButtonX = 47d;

    /// <summary>
    /// 起点区域发现禁区后重新选点的次数上限。
    /// </summary>
    private const int MaxStartValidationAttempts = 5;

    /// <summary>
    /// 从排名前多少百分比的候选中随机选起点，避免每次拖动都从同一位置开始。
    /// </summary>
    private const int StartCandidateRandomTopPercent = 30;

    /// <summary>
    /// 起点探测的模板匹配区域边长（1080P 像素），需要覆盖图标完整尺寸。
    /// </summary>
    private const double StartProbeRegionSize1080P = 400d;

    /// <summary>
    /// 起点探测区域内实际可选起点的边长（1080P 像素），边缘约 65 像素只用于匹配。
    /// </summary>
    private const double StartProbeSelectableRegionSize1080P = 270d;

    /// <summary>
    /// 地图界面上不能作为拖动起点和点击目标的区域（1080P 像素）：左上角菜单、左侧缩放条、顶部任务栏、右下角按钮等。
    /// </summary>
    private static readonly Rect2d[] DangerRects =
    [
        new(0, 0, 400, 430),
        new(0, 430, 110, 260),
        new(930, 0, 990, 100),
        new(1515, 929, 405, 151),
        new(0, 960, 105, 120),
        new(1780, 350, 140, 375),
        new(0, 0, 1920, 20),
    ];

    /// <summary>
    /// 至冬地图底部额外的按钮区域（1080P 像素）。
    /// </summary>
    private static readonly Rect2d SnezhnayaDangerRect = new(797, 984, 330, 96);

    /// <summary>
    /// 运行环境。
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 主程序传送配置：地图比例因子、缩放精度和缩放滑块位置。
    /// </summary>
    private readonly TpConfig _tpConfig;

    /// <summary>
    /// 本次传送的参数。
    /// </summary>
    private readonly ExperimentalTeleportParameters _parameters;

    /// <summary>
    /// 插件日志。
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// 取消令牌。
    /// </summary>
    private readonly CancellationToken _ct;

    /// <summary>
    /// 一次拖动的结果。
    /// </summary>
    /// <param name="InputDeltaX">输入的拖动距离 x（捕获区域像素）。</param>
    /// <param name="InputDeltaY">输入的拖动距离 y。</param>
    /// <param name="CursorDeltaX">回读的光标实际位移 x（捕获区域像素）。</param>
    /// <param name="CursorDeltaY">回读的光标实际位移 y。</param>
    /// <param name="StartX">起点 x。</param>
    /// <param name="StartY">起点 y。</param>
    /// <param name="EndX">终点 x。</param>
    /// <param name="EndY">终点 y。</param>
    public readonly record struct DragResult(
        double InputDeltaX,
        double InputDeltaY,
        double CursorDeltaX,
        double CursorDeltaY,
        double StartX,
        double StartY,
        double EndX,
        double EndY)
    {
        /// <summary>
        /// 光标是否确实移动了。
        /// </summary>
        public bool Moved => Math.Abs(CursorDeltaX) + Math.Abs(CursorDeltaY) >= 2d;

        /// <summary>
        /// 是否选出了有效的起点。没有安全跑道时起点为 (0, 0)。
        /// </summary>
        public bool HasStart => double.IsFinite(StartX) && double.IsFinite(StartY) && Math.Abs(StartX) + Math.Abs(StartY) >= 1d;
    }

    /// <summary>
    /// 起点候选。
    /// </summary>
    /// <param name="Point">起点（捕获区域像素）。</param>
    /// <param name="BoundaryDistance">沿拖动方向到安全边界的距离。</param>
    /// <param name="Length">本次可以拖动的长度。</param>
    /// <param name="CanComplete">是否能一次拖完请求的距离。</param>
    /// <param name="SafetySlack">拖完后剩余的余量。</param>
    /// <param name="Order">生成顺序，排序时保持稳定。</param>
    private readonly record struct StartCandidate(
        Point2d Point,
        double BoundaryDistance,
        double Length,
        bool CanComplete,
        double SafetySlack,
        int Order);

    /// <summary>
    /// 创建拖动器。
    /// </summary>
    public MapDragger(GameRuntime runtime, TpConfig tpConfig, ExperimentalTeleportParameters parameters, ILogger logger, CancellationToken ct)
    {
        _runtime = runtime;
        _tpConfig = tpConfig;
        _parameters = parameters;
        _logger = logger;
        _ct = ct;
    }

    /// <summary>
    /// 捕获区域按不超过 1080P 缩放后的范围。
    /// </summary>
    private Rect CaptureRect => _runtime.SystemInfo.ScaleMax1080PCaptureRect;

    /// <summary>
    /// 地图中心为 center、缩放为 zoomLevel 时，目标是否已经在安全点击区内（用于提前结束拖动）。
    /// </summary>
    /// <param name="targetX">目标 x（原神坐标系）。</param>
    /// <param name="targetY">目标 y（原神坐标系）。</param>
    /// <param name="center">地图中心（原神坐标系）。</param>
    /// <param name="zoomLevel">缩放等级。</param>
    /// <param name="country">目标国家，至冬有额外的危险区。</param>
    public bool IsTargetSafelyClickable(double targetX, double targetY, Point2f center, double zoomLevel, string? country)
    {
        if (zoomLevel <= 0)
        {
            return false;
        }

        // 原神坐标系的 x、y 与屏幕方向相反
        var rect = CaptureRect;
        var clickX = rect.Width / 2d - _tpConfig.MapScaleFactor * (targetX - center.X) / zoomLevel;
        var clickY = rect.Height / 2d - _tpConfig.MapScaleFactor * (targetY - center.Y) / zoomLevel;
        return IsSafePoint(clickX, clickY, rect.Width, rect.Height, EarlyStopMargin, country);
    }

    /// <summary>
    /// 按请求距离能否一次拖到（存在足够长的安全跑道）。
    /// </summary>
    /// <param name="requestedDeltaX">请求的拖动距离 x（捕获区域像素）。</param>
    /// <param name="requestedDeltaY">请求的拖动距离 y。</param>
    /// <param name="country">目标国家。</param>
    public bool CanCompleteSingleDrag(double requestedDeltaX, double requestedDeltaY, string? country)
    {
        var captureRect = CaptureRect;
        var desiredX = requestedDeltaX * _parameters.DragDistanceCorrection;
        var desiredY = requestedDeltaY * _parameters.DragDistanceCorrection;
        var desiredDistance = Math.Sqrt(desiredX * desiredX + desiredY * desiredY);
        if (!double.IsFinite(desiredDistance))
        {
            return false;
        }

        var candidates = GetOrderedStartCandidates(desiredX, desiredY, captureRect.Width, captureRect.Height, country, null, null);
        return candidates.Count > 0 && candidates[0].Length + 1e-6d >= desiredDistance;
    }

    /// <summary>
    /// 拖动地图。在安全区内选起点，必要时用 <paramref name="forbiddenStartProbe"/> 检查起点附近是否有图标（按下图标会打开详情而不是拖动）。
    /// 没有安全跑道时返回 default（不按下鼠标）。
    /// </summary>
    /// <param name="requestedDeltaX">请求的拖动距离 x（捕获区域像素，已除以 Windows 显示缩放）。</param>
    /// <param name="requestedDeltaY">请求的拖动距离 y。</param>
    /// <param name="country">目标国家。</param>
    /// <param name="forbiddenStartRects">不能作为起点的区域（1080P 像素）。</param>
    /// <param name="forbiddenStartProbe">探测起点区域内的禁区，参数为探测区域（捕获区域像素），返回禁区（1080P 像素）。</param>
    /// <param name="attemptedStartCandidates">本轮已经尝试过的起点，避免重复。</param>
    public async Task<DragResult> DragAsync(
        double requestedDeltaX,
        double requestedDeltaY,
        string? country,
        IReadOnlyList<Rect2d>? forbiddenStartRects = null,
        Func<Rect2d, IReadOnlyList<Rect2d>>? forbiddenStartProbe = null,
        ISet<(int X, int Y)>? attemptedStartCandidates = null)
    {
        // 目标已经到位时无需按下鼠标，避免产生无效拖动并触发上层重复识别
        if (requestedDeltaX == 0d && requestedDeltaY == 0d)
        {
            return default;
        }

        var captureRect = CaptureRect;
        // 主程序在拖动前把距离除以了 Windows 显示缩放，这里还原为实际屏幕拖动距离
        var dpiScale = double.IsFinite(_runtime.SystemInfo.DpiScale) && _runtime.SystemInfo.DpiScale > 0
            ? _runtime.SystemInfo.DpiScale
            : 1d;
        var effectiveDistanceCorrection = _parameters.DragDistanceCorrection * dpiScale;
        var desiredX = requestedDeltaX * effectiveDistanceCorrection;
        var desiredY = requestedDeltaY * effectiveDistanceCorrection;
        var activeForbiddenStartRects = forbiddenStartRects?.ToList() ?? [];
        var startValidationAttempts = 0;
        var probedStartCandidates = new HashSet<(int X, int Y)>();
        Point2d start;
        Point2d end;

        // 1. 选起点：先从排名靠前的候选中随机挑一个区域探测禁区，再在区域内部选点
        while (true)
        {
            var rankedAreaCenters = GetOrderedStartCandidates(desiredX, desiredY, captureRect.Width, captureRect.Height,
                country, activeForbiddenStartRects, attemptedStartCandidates);
            if (rankedAreaCenters.Count == 0)
            {
                LogDetailed(
                    "实验传送无法生成安全拖动跑道：requested=({RequestedX:0.0},{RequestedY:0.0}) adjusted=({AdjustedX:0.0},{AdjustedY:0.0}) country={Country}",
                    requestedDeltaX, requestedDeltaY, desiredX, desiredY, country ?? "未指定");
                return default;
            }

            var poolSize = Math.Max(1, (int)Math.Ceiling(rankedAreaCenters.Count * StartCandidateRandomTopPercent / 100d));
            var unprobedAreas = rankedAreaCenters.Take(poolSize)
                .Where(candidate => !probedStartCandidates.Contains(GetStartCandidateKey(candidate.Point)))
                .ToList();
            if (unprobedAreas.Count == 0)
            {
                unprobedAreas = rankedAreaCenters
                    .Where(candidate => !probedStartCandidates.Contains(GetStartCandidateKey(candidate.Point)))
                    .ToList();
            }

            if (unprobedAreas.Count == 0)
            {
                LogDetailed("实验传送起点候选区域均已匹配，停止重复探测");
                return default;
            }

            var areaCenter = unprobedAreas[Random.Shared.Next(unprobedAreas.Count)];
            var areaRect = CreateStartProbeRegion(areaCenter.Point, captureRect.Width, captureRect.Height, StartProbeRegionSize1080P);
            var selectableRect = CreateStartProbeRegion(areaCenter.Point, captureRect.Width, captureRect.Height, StartProbeSelectableRegionSize1080P);
            probedStartCandidates.Add(GetStartCandidateKey(areaCenter.Point));

            if (forbiddenStartProbe is not null)
            {
                var discoveredForbiddenRects = forbiddenStartProbe(areaRect);
                if (discoveredForbiddenRects is { Count: > 0 })
                {
                    activeForbiddenStartRects.AddRange(discoveredForbiddenRects);
                    startValidationAttempts++;
                    LogDetailed(
                        "实验传送起点区域匹配发现禁区，重新选点：attempt={Attempt}/{MaxAttempts} added={AddedCount} total={TotalCount}",
                        startValidationAttempts, MaxStartValidationAttempts, discoveredForbiddenRects.Count, activeForbiddenStartRects.Count);
                }
            }

            var rankedStarts = GetOrderedStartCandidates(desiredX, desiredY, captureRect.Width, captureRect.Height,
                country, activeForbiddenStartRects, attemptedStartCandidates, selectableRect);
            if (rankedStarts.Count == 0)
            {
                if (startValidationAttempts >= MaxStartValidationAttempts)
                {
                    return default;
                }

                continue;
            }

            var startPoolSize = Math.Max(1, (int)Math.Ceiling(rankedStarts.Count * StartCandidateRandomTopPercent / 100d));
            var startPool = rankedStarts.Take(startPoolSize).ToList();
            var selected = startPool[Random.Shared.Next(startPool.Count)];
            attemptedStartCandidates?.Add(GetStartCandidateKey(selected.Point));
            var distance = Math.Sqrt(desiredX * desiredX + desiredY * desiredY);
            var directionX = distance <= 1e-6d ? 0d : desiredX / distance;
            var directionY = distance <= 1e-6d ? 0d : desiredY / distance;
            start = selected.Point;
            end = new Point2d(selected.Point.X + directionX * selected.Length, selected.Point.Y + directionY * selected.Length);
            break;
        }

        // 2. 沿跑道分步拖动
        var inputDistance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
        LogDetailed(
            "实验传送开始拖动：requested=({RequestedX:0.0},{RequestedY:0.0}) runway=({StartX:0.0},{StartY:0.0})->({EndX:0.0},{EndY:0.0}) " +
            "correction={Correction:0.000} forbiddenCount={ForbiddenCount}",
            requestedDeltaX, requestedDeltaY, start.X, start.Y, end.X, end.Y, effectiveDistanceCorrection, activeForbiddenStartRects.Count);
        var (cursorX, cursorY) = await DragAlongAsync(start, end, inputDistance);
        LogDetailed(
            "实验传送拖动完成：input=({InputX:0.0},{InputY:0.0}) cursor=({CursorX:0.0},{CursorY:0.0})",
            end.X - start.X, end.Y - start.Y, cursorX, cursorY);
        return new DragResult(end.X - start.X, end.Y - start.Y, cursorX, cursorY, start.X, start.Y, end.X, end.Y);
    }

    /// <summary>
    /// 沿上一次拖动的同一条跑道反向拖回，不重新选择或识别起点。用于拖到黑色无地图区域后退回。
    /// </summary>
    /// <param name="previous">上一次拖动的结果。</param>
    public async Task<DragResult> ReverseDragAsync(DragResult previous)
    {
        if (!previous.Moved
            || !double.IsFinite(previous.StartX) || !double.IsFinite(previous.StartY)
            || !double.IsFinite(previous.EndX) || !double.IsFinite(previous.EndY))
        {
            return default;
        }

        var start = new Point2d(previous.EndX, previous.EndY);
        var end = new Point2d(previous.StartX, previous.StartY);
        var inputDistance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
        if (!double.IsFinite(inputDistance) || inputDistance < 1d)
        {
            return default;
        }

        _logger.LogWarning(
            "实验传送检测到地图亮度过低，沿原跑道反向拖动：runway=({StartX:0.0},{StartY:0.0})->({EndX:0.0},{EndY:0.0})",
            start.X, start.Y, end.X, end.Y);
        var (cursorX, cursorY) = await DragAlongAsync(start, end, inputDistance);
        return new DragResult(end.X - start.X, end.Y - start.Y, cursorX, cursorY, start.X, start.Y, end.X, end.Y);
    }

    /// <summary>
    /// 拖动左侧缩放滑块，把缩放等级从 zoomLevel 调到 targetZoomLevel。
    /// </summary>
    /// <param name="zoomLevel">当前缩放等级，无效值时重新识别。</param>
    /// <param name="targetZoomLevel">目标缩放等级。</param>
    /// <param name="recognizeZoomLevel">识别当前缩放等级。</param>
    public async Task AdjustZoomAsync(double zoomLevel, double targetZoomLevel, Func<double> recognizeZoomLevel)
    {
        if (!double.IsFinite(zoomLevel))
        {
            zoomLevel = recognizeZoomLevel();
        }

        zoomLevel = Math.Clamp(zoomLevel, 1d, 6d);
        targetZoomLevel = Math.Clamp(targetZoomLevel, 1d, 6d);
        if (Math.Abs(zoomLevel - targetZoomLevel) <= _tpConfig.PrecisionThreshold)
        {
            return;
        }

        // 缩放条上 1.0 在最上方、6.0 在最下方，按线性位置换算
        double zoomStartY = _tpConfig.ZoomStartY;
        double zoomEndY = _tpConfig.ZoomEndY;
        var initialY = zoomStartY + (zoomEndY - zoomStartY) * (zoomLevel - 1d) / 5d;
        var targetY = zoomStartY + (zoomEndY - zoomStartY) * (targetZoomLevel - 1d) / 5d;
        var buttonX = ZoomButtonX + 10d;
        var realRect = _runtime.SystemInfo.CaptureAreaRect;
        var realScale = Math.Max(1e-6d, realRect.Width / 1920d);

        LogDetailed(
            "实验传送滑块缩放：before={BeforeZoom:0.00} target={TargetZoom:0.00} from=({StartX:0.0},{InitialY:0.0}) to=({TargetX:0.0},{TargetY:0.0}) scale={Scale:0.000}",
            zoomLevel, targetZoomLevel, buttonX, initialY, buttonX, targetY, realScale);

        DesktopRegion.DesktopRegionMove(realRect.X + buttonX * realScale, realRect.Y + initialY * realScale);
        await _runtime.Delay(OperationInterval, _ct);
        try
        {
            InputHub.Foreground.Mouse.LeftButtonDown();
            await _runtime.Delay(_parameters.DragStartDelayMilliseconds, _ct);
            DesktopRegion.DesktopRegionMove(realRect.X + buttonX * realScale, realRect.Y + targetY * realScale);
            await _runtime.Delay(_parameters.DragReleaseDelayMilliseconds, _ct);
        }
        finally
        {
            InputHub.Foreground.Mouse.LeftButtonUp();
        }

        // 松开后等待缩放动画结束再识别
        await _runtime.Delay(OperationInterval, _ct);
    }

    /// <summary>
    /// 判断点是否在安全区内：离屏幕边缘和危险区至少 margin（1080P 像素）。
    /// </summary>
    /// <param name="x">点 x（捕获区域像素）。</param>
    /// <param name="y">点 y。</param>
    /// <param name="width">捕获区域宽度。</param>
    /// <param name="height">捕获区域高度。</param>
    /// <param name="margin">保留距离（1080P 像素）。</param>
    /// <param name="country">目标国家，至冬有额外的危险区。</param>
    public static bool IsSafePoint(double x, double y, int width, int height, double margin, string? country)
    {
        var scaleX = width / 1920d;
        var scaleY = height / 1080d;
        if (x < margin * scaleX || x > width - margin * scaleX
            || y < margin * scaleY || y > height - margin * scaleY)
        {
            return false;
        }

        if (DangerRects.Any(danger => ContainsExpanded(danger, x, y, scaleX, scaleY, margin)))
        {
            return false;
        }

        return !IsSnezhnaya(country) || !ContainsExpanded(SnezhnayaDangerRect, x, y, scaleX, scaleY, margin);
    }

    /// <summary>
    /// 按下左键沿直线从 start 拖到 end（捕获区域像素），返回回读的光标实际位移。
    /// </summary>
    private async Task<(double X, double Y)> DragAlongAsync(Point2d start, Point2d end, double inputDistance)
    {
        var captureRect = CaptureRect;
        var realCaptureRect = _runtime.SystemInfo.CaptureAreaRect;
        var maxStep = _parameters.MaxSingleStepDistancePixels;
        var steps = Math.Max(1, maxStep > 0 ? (int)Math.Ceiling(inputDistance / maxStep) : 1);

        MoveToCapturePoint(start, captureRect, realCaptureRect);
        await _runtime.Delay(OperationInterval, _ct);
        User32.GetCursorPos(out var cursorBefore);
        try
        {
            InputHub.Foreground.Mouse.LeftButtonDown();
            await _runtime.Delay(_parameters.DragStartDelayMilliseconds, _ct);
            for (var i = 1; i <= steps; i++)
            {
                _ct.ThrowIfCancellationRequested();
                var progress = i / (double)steps;
                MoveToCapturePoint(
                    new Point2d(start.X + (end.X - start.X) * progress, start.Y + (end.Y - start.Y) * progress),
                    captureRect,
                    realCaptureRect);
                // 最后一步停留更久再松开，避免地图惯性把松开前的位移吞掉
                await _runtime.Delay(i < steps ? _parameters.DragStepIntervalMilliseconds : _parameters.DragReleaseDelayMilliseconds, _ct);
            }
        }
        finally
        {
            InputHub.Foreground.Mouse.LeftButtonUp();
        }

        await _runtime.Delay(OperationInterval, _ct);
        User32.GetCursorPos(out var cursorAfter);
        var actualX = (cursorAfter.X - cursorBefore.X) * captureRect.Width / Math.Max(1d, realCaptureRect.Width);
        var actualY = (cursorAfter.Y - cursorBefore.Y) * captureRect.Height / Math.Max(1d, realCaptureRect.Height);
        return (actualX, actualY);
    }

    /// <summary>
    /// 鼠标按下、松开前后的等待。
    /// </summary>
    private int OperationInterval => _parameters.OperationDelay(50);

    /// <summary>
    /// 生成并排序起点候选：能一次拖完的优先（余量大的在前），否则按可拖距离从长到短。
    /// </summary>
    private static List<StartCandidate> GetOrderedStartCandidates(
        double requestedX,
        double requestedY,
        int width,
        int height,
        string? country,
        IReadOnlyList<Rect2d>? forbiddenStartRects,
        ISet<(int X, int Y)>? attemptedStartCandidates,
        Rect2d? candidateRegion = null)
    {
        var requestedDistance = Math.Sqrt(requestedX * requestedX + requestedY * requestedY);
        if (!double.IsFinite(requestedDistance) || width <= 0 || height <= 0)
        {
            return [];
        }

        var directionX = requestedDistance <= 1e-6d ? 0d : requestedX / requestedDistance;
        var directionY = requestedDistance <= 1e-6d ? 0d : requestedY / requestedDistance;
        var rankedCandidates = new List<StartCandidate>();
        var order = 0;
        foreach (var candidate in GetStartCandidatePoints(width, height, requestedX, requestedY, country, forbiddenStartRects, candidateRegion))
        {
            if (!IsSafePoint(candidate.X, candidate.Y, width, height, SafeMargin, country)
                || IsForbiddenStartPoint(candidate, forbiddenStartRects, width, height))
            {
                continue;
            }

            if (attemptedStartCandidates is not null && attemptedStartCandidates.Contains(GetStartCandidateKey(candidate)))
            {
                continue;
            }

            var boundaryDistance = GetScreenBoundaryDistance(candidate, directionX, directionY, width, height);
            var length = Math.Min(requestedDistance, boundaryDistance);
            if (!double.IsFinite(length) || length < 0d)
            {
                continue;
            }

            rankedCandidates.Add(new StartCandidate(
                candidate,
                boundaryDistance,
                length,
                boundaryDistance + 1e-6d >= requestedDistance,
                boundaryDistance - requestedDistance,
                order++));
        }

        return rankedCandidates.Any(candidate => candidate.CanComplete)
            ? rankedCandidates
                .Where(candidate => candidate.CanComplete)
                .OrderByDescending(candidate => candidate.SafetySlack)
                .ThenBy(candidate => candidate.Order)
                .ToList()
            : rankedCandidates
                .OrderByDescending(candidate => candidate.BoundaryDistance)
                .ThenBy(candidate => candidate.Order)
                .ToList();
    }

    /// <summary>
    /// 起点去重用的键。
    /// </summary>
    private static (int X, int Y) GetStartCandidateKey(Point2d point) => ((int)Math.Round(point.X), (int)Math.Round(point.Y));

    /// <summary>
    /// 生成起点候选点：屏幕的若干分位线、危险区与禁区的边界线两两组合，再加上目标反方向的边缘点。
    /// 指定 candidateRegion 时只在区域内部均匀取点。
    /// </summary>
    private static IReadOnlyList<Point2d> GetStartCandidatePoints(
        int width,
        int height,
        double requestedX,
        double requestedY,
        string? country,
        IReadOnlyList<Rect2d>? forbiddenStartRects,
        Rect2d? candidateRegion)
    {
        var scaleX = width / 1920d;
        var scaleY = height / 1080d;
        var minX = SafeMargin * scaleX;
        var maxX = width - SafeMargin * scaleX;
        var minY = SafeMargin * scaleY;
        var maxY = height - SafeMargin * scaleY;
        List<double> xValues = candidateRegion is { } region
            ? CreateRegionSamples(region.X, region.Right)
            : [minX, minX + 1d, width * 0.25d, width * 0.5d, width * 0.75d, maxX - 1d, maxX];
        List<double> yValues = candidateRegion is { } regionY
            ? CreateRegionSamples(regionY.Y, regionY.Bottom)
            : [minY, minY + 1d, height * 0.25d, height * 0.5d, height * 0.75d, maxY - 1d, maxY];

        if (candidateRegion is { } selectedRegion)
        {
            minX = Math.Max(minX, selectedRegion.X);
            maxX = Math.Min(maxX, selectedRegion.Right);
            minY = Math.Max(minY, selectedRegion.Y);
            maxY = Math.Min(maxY, selectedRegion.Bottom);
        }

        void AddExcludedRectBoundaries(Rect2d rect, double margin)
        {
            xValues.Add((rect.X - margin) * scaleX - 1d);
            xValues.Add((rect.Right + margin) * scaleX + 1d);
            yValues.Add((rect.Y - margin) * scaleY - 1d);
            yValues.Add((rect.Bottom + margin) * scaleY + 1d);
        }

        foreach (var danger in DangerRects)
        {
            AddExcludedRectBoundaries(danger, SafeMargin);
        }

        if (IsSnezhnaya(country))
        {
            AddExcludedRectBoundaries(SnezhnayaDangerRect, SafeMargin);
        }

        if (forbiddenStartRects is not null)
        {
            foreach (var forbidden in forbiddenStartRects)
            {
                AddExcludedRectBoundaries(forbidden, 0d);
            }
        }

        var candidates = new List<Point2d>(xValues.Count * yValues.Count + 4);
        foreach (var x in xValues)
        {
            foreach (var y in yValues)
            {
                candidates.Add(new Point2d(Math.Clamp(x, minX, maxX), Math.Clamp(y, minY, maxY)));
            }
        }

        // 目标反方向的边缘点优先覆盖大位移场景；区域内选点时严格限制在内部区域
        var distance = Math.Sqrt(requestedX * requestedX + requestedY * requestedY);
        if (candidateRegion is null && distance > 1e-6d && double.IsFinite(distance))
        {
            var directionX = requestedX / distance;
            var directionY = requestedY / distance;
            candidates.Add(new Point2d(
                directionX > 0d ? minX : directionX < 0d ? maxX : width * 0.5d,
                directionY > 0d ? minY : directionY < 0d ? maxY : height * 0.5d));
        }

        if (candidateRegion is null)
        {
            candidates.Add(new Point2d(width * 0.5d, height * 0.55d));
            candidates.Add(new Point2d(width * 0.38d, height * 0.72d));
            candidates.Add(new Point2d(width * 0.62d, height * 0.72d));
        }

        return candidates
            .GroupBy(candidate => ((int)Math.Round(candidate.X), (int)Math.Round(candidate.Y)))
            .Select(group => group.First())
            .ToList();

        static List<double> CreateRegionSamples(double left, double right)
        {
            var size = Math.Max(0d, right - left);
            return Enumerable.Range(1, 5).Select(index => left + size * index / 6d).ToList();
        }
    }

    /// <summary>
    /// 以 center 为中心、边长 size1080P（1080P 像素）的探测区域（捕获区域像素）。
    /// </summary>
    private static Rect2d CreateStartProbeRegion(Point2d center, int width, int height, double size1080P)
    {
        var sizeX = size1080P * width / 1920d;
        var sizeY = size1080P * height / 1080d;
        return new Rect2d(center.X - sizeX / 2d, center.Y - sizeY / 2d, sizeX, sizeY);
    }

    /// <summary>
    /// 从 start 沿方向走到安全边界的距离。
    /// </summary>
    private static double GetScreenBoundaryDistance(Point2d start, double directionX, double directionY, int width, int height)
    {
        var scaleX = width / 1920d;
        var scaleY = height / 1080d;
        var minX = SafeMargin * scaleX;
        var maxX = width - SafeMargin * scaleX;
        var minY = SafeMargin * scaleY;
        var maxY = height - SafeMargin * scaleY;
        var distance = double.PositiveInfinity;
        if (directionX > 1e-9d)
        {
            distance = Math.Min(distance, (maxX - start.X) / directionX);
        }
        else if (directionX < -1e-9d)
        {
            distance = Math.Min(distance, (minX - start.X) / directionX);
        }

        if (directionY > 1e-9d)
        {
            distance = Math.Min(distance, (maxY - start.Y) / directionY);
        }
        else if (directionY < -1e-9d)
        {
            distance = Math.Min(distance, (minY - start.Y) / directionY);
        }

        return Math.Max(0d, distance);
    }

    /// <summary>
    /// 点是否落在禁区（1080P 像素）内。
    /// </summary>
    private static bool IsForbiddenStartPoint(Point2d point, IReadOnlyList<Rect2d>? forbiddenStartRects, int width, int height)
    {
        if (forbiddenStartRects is null || forbiddenStartRects.Count == 0)
        {
            return false;
        }

        var scaleX = width / 1920d;
        var scaleY = height / 1080d;
        return forbiddenStartRects.Any(rect =>
            point.X >= rect.X * scaleX && point.X <= rect.Right * scaleX
            && point.Y >= rect.Y * scaleY && point.Y <= rect.Bottom * scaleY);
    }

    /// <summary>
    /// 点是否落在向外扩展 margin 后的矩形（1080P 像素）内。
    /// </summary>
    private static bool ContainsExpanded(Rect2d rect, double x, double y, double scaleX, double scaleY, double margin)
    {
        return x >= (rect.X - margin) * scaleX && x <= (rect.Right + margin) * scaleX
               && y >= (rect.Y - margin) * scaleY && y <= (rect.Bottom + margin) * scaleY;
    }

    /// <summary>
    /// 是否为至冬。
    /// </summary>
    private static bool IsSnezhnaya(string? country) => string.Equals(country, "至冬", StringComparison.Ordinal);

    /// <summary>
    /// 把捕获区域坐标换算为屏幕坐标后移动鼠标。
    /// </summary>
    private static void MoveToCapturePoint(Point2d point, Rect captureRect, RECT realCaptureRect)
    {
        var x = Math.Clamp(point.X, 0d, captureRect.Width);
        var y = Math.Clamp(point.Y, 0d, captureRect.Height);
        DesktopRegion.DesktopRegionMove(
            realCaptureRect.X + x * realCaptureRect.Width / Math.Max(1d, captureRect.Width),
            realCaptureRect.Y + y * realCaptureRect.Height / Math.Max(1d, captureRect.Height));
    }

    /// <summary>
    /// 输出详细调试日志。
    /// </summary>
    private void LogDetailed(string message, params object?[] args)
    {
        if (_parameters.DetailedLogs)
        {
#pragma warning disable CA2254 // 调试日志的模板由调用方给出
            _logger.LogDebug(message, args);
#pragma warning restore CA2254
        }
    }
}
