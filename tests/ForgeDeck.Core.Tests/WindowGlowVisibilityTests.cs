using ForgeDeck.Core;

namespace ForgeDeck.Core.Tests;

public class WindowGlowVisibilityTests
{
    [Fact]
    public void NormalIdle_Shows() =>
        Assert.True(WindowGlowVisibility.ShouldShow(ownerVisible: true, maximized: false, minimized: false, motionBusy: false));

    [Fact]
    public void Maximized_Hides() =>
        Assert.False(WindowGlowVisibility.ShouldShow(ownerVisible: true, maximized: true, minimized: false, motionBusy: false));

    [Fact]
    public void Minimized_Hides() =>
        Assert.False(WindowGlowVisibility.ShouldShow(ownerVisible: true, maximized: false, minimized: true, motionBusy: false));

    [Fact]
    public void HiddenOwner_Hides() =>
        Assert.False(WindowGlowVisibility.ShouldShow(ownerVisible: false, maximized: false, minimized: false, motionBusy: false));

    [Fact]
    public void MaximizeAnimationStillNormal_Hides() =>
        Assert.False(WindowGlowVisibility.ShouldShow(ownerVisible: true, maximized: false, minimized: false, motionBusy: true));

    [Fact]
    public void RestoreAnimationAlreadyNormal_HidesUntilIdle() =>
        Assert.False(WindowGlowVisibility.ShouldShow(ownerVisible: true, maximized: false, minimized: false, motionBusy: true));
}
