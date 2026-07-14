namespace BuzzahBuddy.Services.AppLifecycle;

/// <inheritdoc />
public class AppLifecycleService : IAppLifecycleService
{
    public event EventHandler? Stopped;
    public event EventHandler? Resumed;
    public void NotifyStopped() => Stopped?.Invoke(this, EventArgs.Empty);
    public void NotifyResumed() => Resumed?.Invoke(this, EventArgs.Empty);
}
