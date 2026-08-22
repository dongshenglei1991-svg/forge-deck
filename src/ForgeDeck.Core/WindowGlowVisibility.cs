namespace ForgeDeck.Core;

/// <summary>主窗口外侧光晕是否显示。最大化 / 最小化 / 最大化动画期间贴着工作区，没有外侧可画。</summary>
public static class WindowGlowVisibility
{
    public static bool ShouldShow(bool ownerVisible, bool maximized, bool minimized, bool motionBusy)
        => ownerVisible && !maximized && !minimized && !motionBusy;
}
