namespace BitMagic.BennyBox.Core.Services;

// Stops the device idling into sleep while DownloadManager has work to do. Platforms without an
// implementation get NoOpSleepInhibitor, see SharedServiceRegistration.
public interface ISleepInhibitor
{
    void SetActive(bool active);
}

public sealed class NoOpSleepInhibitor : ISleepInhibitor
{
    public void SetActive(bool active)
    {
    }
}
