using OpenNetMeter.PlatformAbstractions;

namespace OpenNetMeter.Services;

public sealed class PlaceholderStartupRegistrationService : IStartupRegistrationService
{
    public bool IsEnabled() => false;

    public void EnsureUpToDate(bool startMinimized)
    {
    }

    public void SetEnabled(bool enabled, bool startMinimized)
    {
    }
}

