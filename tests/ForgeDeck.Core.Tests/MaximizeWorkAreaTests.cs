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
}
