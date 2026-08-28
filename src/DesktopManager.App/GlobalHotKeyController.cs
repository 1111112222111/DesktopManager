using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DesktopManager.Core;

namespace DesktopManager.App;

internal sealed partial class GlobalHotKeyController : IDisposable
{
    private const int HotKeyId = 0x444D;
    private const int WmHotKey = 0x0312;

    private readonly Window _window;
    private GlobalHotKeyBinding _binding;
    private readonly Action _activated;
    private readonly Action<string> _registrationFailed;
    private HwndSource? _source;
    private nint _windowHandle;
    private bool _isRegistered;
    private bool _disposed;

    public GlobalHotKeyBinding CurrentBinding => _binding;
    public bool IsRegistered => _isRegistered;

    public GlobalHotKeyController(
        Window window,
        GlobalHotKeyBinding binding,
        Action activated,
        Action<string> registrationFailed)
    {
        _window = window;
        _binding = binding;
        _activated = activated;
        _registrationFailed = registrationFailed;
        _window.SourceInitialized += Window_SourceInitialized;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (_disposed || _windowHandle != nint.Zero)
        {
            return;
        }

        _windowHandle = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(WindowMessageHook);
        if (!TryRegister(_binding))
        {
            var unavailable = _binding;
            if (unavailable != GlobalHotKeyBinding.Default
                && TryRegister(GlobalHotKeyBinding.Default))
            {
                _binding = GlobalHotKeyBinding.Default;
                _registrationFailed(
                    $"全局快捷键 {unavailable.DisplayText} 已被其他程序占用；"
                    + $"当前回退为 {GlobalHotKeyBinding.Default.DisplayText}。");
            }
            else
            {
                _registrationFailed($"全局快捷键 {_binding.DisplayText} 已被其他程序占用。");
            }
        }
    }

    public bool TryChangeBinding(GlobalHotKeyBinding binding, out string message)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (_disposed)
        {
            message = "快捷键注册器已经关闭。";
            return false;
        }
        if (_windowHandle == nint.Zero)
        {
            _binding = binding;
            message = $"全局快捷键已设置为 {binding.DisplayText}。";
            return true;
        }
        if (_isRegistered && _binding == binding)
        {
            message = $"全局快捷键仍为 {binding.DisplayText}。";
            return true;
        }

        var previous = _binding;
        if (_isRegistered)
        {
            if (!UnregisterHotKey(_windowHandle, HotKeyId))
            {
                message = $"Windows 未能释放 {previous.DisplayText}；已保留原快捷键。";
                return false;
            }
            _isRegistered = false;
        }
        if (TryRegister(binding))
        {
            _binding = binding;
            message = $"全局快捷键已更新为 {binding.DisplayText}。";
            return true;
        }

        var rolledBack = TryRegister(previous);
        _binding = previous;
        message = rolledBack
            ? $"{binding.DisplayText} 已被其他程序占用；已恢复 {previous.DisplayText}。"
            : $"{binding.DisplayText} 已被占用，且无法恢复 {previous.DisplayText}；当前没有活动的全局快捷键。";
        return false;
    }

    private bool TryRegister(GlobalHotKeyBinding binding)
    {
        _isRegistered = RegisterHotKey(
            _windowHandle,
            HotKeyId,
            binding.NativeModifiers,
            binding.VirtualKey);
        return _isRegistered;
    }

    private nint WindowMessageHook(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == WmHotKey && wParam == HotKeyId)
        {
            handled = true;
            _activated();
        }

        return nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.SourceInitialized -= Window_SourceInitialized;
        if (_windowHandle != nint.Zero && _isRegistered)
        {
            _ = UnregisterHotKey(_windowHandle, HotKeyId);
        }
        _source?.RemoveHook(WindowMessageHook);

        _source = null;
        _windowHandle = nint.Zero;
        _isRegistered = false;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(
        nint windowHandle,
        int id,
        uint modifiers,
        uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint windowHandle, int id);
}
