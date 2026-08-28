namespace DesktopManager.Infrastructure;

public interface IStartupRegistration
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

public interface IStartupValueStore
{
    string? Read();

    void Write(string value);

    void Delete();
}

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private readonly IStartupValueStore _valueStore;
    private readonly string _launchCommand;

    public WindowsStartupRegistration(IStartupValueStore valueStore, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(valueStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _valueStore = valueStore;
        _launchCommand = $"\"{Path.GetFullPath(executablePath)}\" --background";
    }

    public bool IsEnabled => string.Equals(
        _valueStore.Read(),
        _launchCommand,
        StringComparison.OrdinalIgnoreCase);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            _valueStore.Write(_launchCommand);
            return;
        }

        _valueStore.Delete();
    }
}
