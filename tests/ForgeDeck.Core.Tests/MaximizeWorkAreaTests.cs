using ForgeDeck.Core;

namespace ForgeDeck.Core.Tests;

public class MaximizeWorkAreaTests
{
    [Fact]
    public void BottomTaskbar_UsesWorkHeightNotFullMonitor()
    {
        // 1920×1080，底部 40px 任务栏 → 工作区高度 1040
        var p = MaximizeWorkArea.FromMonitor(
            workLeft: 0, workTop: 0, workRight: 1920, workBottom: 1040,
            monitorLeft: 0, monitorTop: 0);

        Assert.Equal(0, p.X);
        Assert.Equal(0, p.Y);
        Assert.Equal(1920, p.Width);
        Assert.Equal(1040, p.Height);
    }

    [Fact]
    public void LeftTaskbar_OffsetsOriginAndShrinksWidth()
    {
        var p = MaximizeWorkArea.FromMonitor(
            workLeft: 80, workTop: 0, workRight: 1920, workBottom: 1080,
            monitorLeft: 0, monitorTop: 0);

        Assert.Equal(80, p.X);
        Assert.Equal(0, p.Y);
        Assert.Equal(1840, p.Width);
        Assert.Equal(1080, p.Height);
    }

    [Fact]
    public void SecondaryMonitor_PositionIsRelativeToMonitorOrigin()
    {
        var p = MaximizeWorkArea.FromMonitor(
            workLeft: 1920, workTop: 0, workRight: 3840, workBottom: 1040,
            monitorLeft: 1920, monitorTop: 0);

        Assert.Equal(0, p.X);
        Assert.Equal(0, p.Y);
        Assert.Equal(1920, p.Width);
        Assert.Equal(1040, p.Height);
    }

    [Fact]
    public void ExpandByFrame_MatchesCaptionMaximizeOverflow()
    {
        // 0e8a644：工作区 2752×1104，WS_CAPTION 下窗口矩形 (-7,-7) 2766×1118
        var work = MaximizeWorkArea.FromMonitor(0, 0, 2752, 1104, 0, 0);
        var p = MaximizeWorkArea.ExpandByFrame(work, 7);
        Assert.Equal(-7, p.X);
        Assert.Equal(-7, p.Y);
        Assert.Equal(2766, p.Width);
        Assert.Equal(1118, p.Height);
    }

    [Fact]
    public void ClientInWorkArea_InflatedWindow_ClientIsWorkArea()
    {
        var client = MaximizeWorkArea.ClientInWorkArea(
            winLeft: -7, winTop: -7, winRight: 2759, winBottom: 1111,
            workLeft: 0, workTop: 0, workRight: 2752, workBottom: 1104);
        Assert.Equal(0, client.Left);
        Assert.Equal(0, client.Top);
        Assert.Equal(2752, client.Right);
        Assert.Equal(1104, client.Bottom);
    }

    [Fact]
    public void ClientInWorkArea_WindowEqualsWork_Unchanged()
    {
        var client = MaximizeWorkArea.ClientInWorkArea(
            80, 0, 1920, 1080,
            80, 0, 1920, 1080);
        Assert.Equal(80, client.Left);
        Assert.Equal(0, client.Top);
        Assert.Equal(1920, client.Right);
        Assert.Equal(1080, client.Bottom);
    }
}
