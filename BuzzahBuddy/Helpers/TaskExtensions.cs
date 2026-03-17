namespace BuzzahBuddy.Helpers;

/// <summary>
/// Extension methods for safe fire-and-forget async patterns.
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Safely executes a Task without awaiting, catching and logging any exceptions.
    /// Use instead of <c>_ = SomeAsync()</c> to prevent silent exception swallowing.
    /// </summary>
    public static async void SafeFireAndForget(
        this Task task,
        string? callerTag = null,
        Action<Exception>? onException = null)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            var tag = callerTag ?? "[FIRE-AND-FORGET]";
            System.Diagnostics.Debug.WriteLine($"{tag} Unobserved async exception: {ex}");
            onException?.Invoke(ex);
        }
    }
}
