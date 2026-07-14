namespace BuzzahBuddy.Services.AppLifecycle;

/// <summary>
/// Broadcasts window Stopped/Resumed lifecycle events so ViewModels can pause
/// BLE polling while backgrounded and resync state on resume.
/// </summary>
public interface IAppLifecycleService
{
    event EventHandler? Stopped;
    event EventHandler? Resumed;
    void NotifyStopped();
    void NotifyResumed();
}
