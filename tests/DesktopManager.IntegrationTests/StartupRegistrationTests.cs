using DesktopManager.Infrastructure;

namespace DesktopManager.IntegrationTests;

public sealed class StartupRegistrationTests
{
    [Fact]
    public void SetEnabled_WritesQuotedBackgroundCommand_AndRemovesItWhenDisabled()
    {
        var values = new InMemoryStartupValueStore();
        IStartupRegistration registration = new WindowsStartupRegistration(
            values,
            @"C:\Program Files\Desktop Manager\DesktopManager.App.exe");

        registration.SetEnabled(true);

        Assert.True(registration.IsEnabled);
        Assert.Equal(
            "\"C:\\Program Files\\Desktop Manager\\DesktopManager.App.exe\" --background",
            values.Value);

        registration.SetEnabled(false);

        Assert.False(registration.IsEnabled);
        Assert.Null(values.Value);
    }

    private sealed class InMemoryStartupValueStore : IStartupValueStore
    {
        public string? Value { get; private set; }

        public string? Read() => Value;

        public void Write(string value) => Value = value;

        public void Delete() => Value = null;
    }
}
