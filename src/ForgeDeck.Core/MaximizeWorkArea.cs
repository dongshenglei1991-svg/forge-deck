namespace ForgeDeck.Core;

/// <summary>无边框窗口最大化尺寸：相对当前显示器，限制在工作区（排除任务栏）。</summary>
public readonly record struct MaxPlacement(int X, int Y, int Width, int Height);

public readonly record struct IntRect(int Left, int Top, int Right, int Bottom);

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

    /// <summary>
    /// WS_CAPTION 最大化的默认窗口矩形：相对工作区四边各外扩尺寸框，客户区才能铺满工作区。
    /// WindowChrome 会把客户区拉平到整个窗口，必须再靠 WM_NCCALCSIZE 把客户区裁回工作区。
    /// </summary>
    public static MaxPlacement ExpandByFrame(MaxPlacement p, int framePx)
    {
        if (framePx <= 0) return p;
        return new(p.X - framePx, p.Y - framePx, p.Width + 2 * framePx, p.Height + 2 * framePx);
    }

    /// <summary>最大化客户区 = 窗口矩形与工作区的交集。外扩的尺寸框落在屏幕外，交集正好是工作区。</summary>
    public static IntRect ClientInWorkArea(
        int winLeft, int winTop, int winRight, int winBottom,
        int workLeft, int workTop, int workRight, int workBottom)
    {
        var left = Math.Max(winLeft, workLeft);
        var top = Math.Max(winTop, workTop);
        var right = Math.Min(winRight, workRight);
        var bottom = Math.Min(winBottom, workBottom);
        if (right <= left || bottom <= top)
            return new IntRect(workLeft, workTop, workRight, workBottom);
        return new IntRect(left, top, right, bottom);
    }
}
