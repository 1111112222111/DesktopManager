using DesktopManager.Core;

namespace DesktopManager.Core.Tests;

public sealed class GlobalHotKeyBindingTests
{
    [Fact]
    public void Default_UsesCtrlAltSpaceWindowsBinding()
    {
        var binding = GlobalHotKeyBinding.Default;

        Assert.Equal("Ctrl + Alt + Space", binding.DisplayText);
        Assert.Equal(0x0003u, binding.NativeModifiers);
        Assert.Equal(0x20u, binding.VirtualKey);
    }

    [Fact]
    public void TryCreate_CanonicalizesSupportedCombination()
    {
        var succeeded = GlobalHotKeyBinding.TryCreate(
            "f8", ctrl: true, alt: false, shift: true, windows: false,
            out var binding, out var message);

        Assert.True(succeeded, message);
        Assert.Equal("Ctrl + Shift + F8", binding!.DisplayText);
        Assert.Equal(0x0006u, binding.NativeModifiers);
        Assert.Equal(0x77u, binding.VirtualKey);
    }

    [Theory]
    [InlineData("A", true, false, false, false)]
    [InlineData("Escape", true, true, false, false)]
    [InlineData("Space", false, false, false, false)]
    public void TryCreate_RejectsUnsafeOrUnsupportedCombination(
        string key,
        bool ctrl,
        bool alt,
        bool shift,
        bool windows)
    {
        var succeeded = GlobalHotKeyBinding.TryCreate(
            key, ctrl, alt, shift, windows, out var binding, out var message);

        Assert.False(succeeded);
        Assert.Null(binding);
        Assert.NotEmpty(message);
    }

    [Fact]
    public void NormalizeOrDefault_RejectsTamperedPersistedBinding()
    {
        var tampered = new GlobalHotKeyBinding("Ctrl + Alt + Escape", 0x0003, 0x1B);

        var normalized = GlobalHotKeyBinding.NormalizeOrDefault(tampered);

        Assert.Equal(GlobalHotKeyBinding.Default, normalized);
    }
}
