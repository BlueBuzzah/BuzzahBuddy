using System.Text.Json.Serialization;

namespace BuzzahBuddy.Models;

/// <summary>
/// How a recorded session ended.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SessionOutcome>))]
public enum SessionOutcome
{
    /// <summary>
    /// The session ran its full planned length.
    /// </summary>
    Completed,

    /// <summary>
    /// The person stopped the session from the app.
    /// </summary>
    StoppedEarly,

    /// <summary>
    /// The session ended while the app was not watching and did not reach its planned length.
    /// </summary>
    EndedEarly
}

/// <summary>
/// One finished therapy session in the history.
/// </summary>
public sealed class SessionRecord
{
    /// <summary>
    /// Unique identifier, kept from the <see cref="OpenSession"/> it was closed from.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// When the session started.
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// When the session ended.
    /// </summary>
    public DateTimeOffset EndedAt { get; set; }

    /// <summary>
    /// Seconds of therapy actually delivered, excluding paused time.
    /// </summary>
    public int TherapySeconds { get; set; }

    /// <summary>
    /// Seconds the session was planned to run.
    /// </summary>
    public int PlannedSeconds { get; set; }

    /// <summary>
    /// Id of the therapy profile that ran.
    /// </summary>
    public int ProfileId { get; set; }

    /// <summary>
    /// Name of the therapy profile that ran.
    /// </summary>
    public string ProfileName { get; set; } = "";

    /// <summary>
    /// How the session ended.
    /// </summary>
    public SessionOutcome Outcome { get; set; }

    /// <summary>
    /// Identifier of the glove the session ran on, when known.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// Local calendar day the session started on.
    /// </summary>
    [JsonIgnore]
    public DateOnly Day => DateOnly.FromDateTime(StartedAt.DateTime);
}

/// <summary>
/// A free-text note attached to a calendar day.
/// </summary>
public sealed class DayNote
{
    /// <summary>
    /// The day the note belongs to.
    /// </summary>
    public DateOnly Day { get; set; }

    /// <summary>
    /// The note text.
    /// </summary>
    public string Text { get; set; } = "";
}
