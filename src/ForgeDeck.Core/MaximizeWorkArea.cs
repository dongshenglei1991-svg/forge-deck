namespace ForgeDeck.Core;

/// <summary>无边框窗口最大化尺寸：相对当前显示器，限制在工作区（排除任务栏）。</summary>
public readonly record struct MaxPlacement(int X, int Y, int Width, int Height);

public static class MaximizeWorkArea
{
    public static MaxPlacement FromMonitor(
        int workLeft, int workTop, int workRight, int workBottom,
        int monitorLeft, int monitorTop)
        => new(
            workLeft - monitorLeft,
            workTop - monitorTop,
            workRight - workLeft,
            workBottom - workTop);
}
