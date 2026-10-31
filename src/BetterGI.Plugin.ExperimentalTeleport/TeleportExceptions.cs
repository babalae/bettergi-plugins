using System;

namespace BetterGI.Plugin.ExperimentalTeleport;

/// <summary>
/// 实验传送的页面状态无法继续推进（交互面板无法识别、确认次数达到上限等）。
/// 由 <see cref="ExperimentalTpTask"/> 的重试按普通传送异常处理：等待后重试本次传送。
/// </summary>
/// <param name="message">原因。</param>
/// <param name="innerException">内部异常。</param>
internal sealed class TeleportTargetLocalizationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// 点击目标后传送按钮位置出现不可传送的提示（点位未解锁等）。
/// 由 <see cref="ExperimentalTpTask"/> 的重试关闭提示后重试，三次都出现时改为“传送点可能未开启”。
/// </summary>
internal sealed class ExperimentalTeleportUnavailableException() : Exception("传送交互提示未出现可传送按钮");
