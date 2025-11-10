namespace BuzzahBuddy.Models;

/// <summary>
/// Represents a parsed response from the BlueBuzzah glove.
/// Responses are in KEY:VALUE format, terminated with \x04 (EOT).
/// </summary>
public class CommandResponse
{
    private readonly Dictionary<string, string> _data = new();

    /// <summary>
    /// Gets whether the response indicates an error.
    /// </summary>
    public bool IsError => _data.ContainsKey("ERROR");

    /// <summary>
    /// Gets the error message if this is an error response.
    /// </summary>
    public string? ErrorMessage => IsError ? _data["ERROR"] : null;

    /// <summary>
    /// Gets whether the response contains any data.
    /// </summary>
    public bool HasData => _data.Count > 0;

    /// <summary>
    /// Gets the raw data dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, string> Data => _data;

    /// <summary>
    /// Parses a raw response string into a CommandResponse object.
    /// </summary>
    /// <param name="rawResponse">The raw response with KEY:VALUE lines and EOT terminator</param>
    /// <returns>Parsed CommandResponse</returns>
    public static CommandResponse Parse(string rawResponse)
    {
        var response = new CommandResponse();

        // Remove EOT character if present
        var cleaned = rawResponse.Replace("\x04", "").Trim();

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return response;
        }

        // Split into lines and parse KEY:VALUE pairs
        var lines = cleaned.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = trimmed.Substring(0, colonIndex).Trim();
                var value = trimmed.Substring(colonIndex + 1).Trim();
                response._data[key] = value;
            }
        }

        return response;
    }

    /// <summary>
    /// Gets a string value for the specified key.
    /// </summary>
    public string? GetString(string key)
    {
        return _data.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Gets an integer value for the specified key.
    /// </summary>
    public int? GetInt(string key)
    {
        if (_data.TryGetValue(key, out var value) && int.TryParse(value, out var result))
        {
            return result;
        }
        return null;
    }

    /// <summary>
    /// Gets a double value for the specified key.
    /// </summary>
    public double? GetDouble(string key)
    {
        if (_data.TryGetValue(key, out var value) && double.TryParse(value, out var result))
        {
            return result;
        }
        return null;
    }

    /// <summary>
    /// Gets a boolean value for the specified key.
    /// Recognizes: "True", "true", "1" as true; "False", "false", "0" as false.
    /// </summary>
    public bool? GetBool(string key)
    {
        if (_data.TryGetValue(key, out var value))
        {
            if (value.Equals("True", StringComparison.OrdinalIgnoreCase) || value == "1")
                return true;
            if (value.Equals("False", StringComparison.OrdinalIgnoreCase) || value == "0")
                return false;
        }
        return null;
    }

    /// <summary>
    /// Checks if the response contains the specified key.
    /// </summary>
    public bool ContainsKey(string key)
    {
        return _data.ContainsKey(key);
    }

    /// <summary>
    /// Gets all keys in the response.
    /// </summary>
    public IEnumerable<string> Keys => _data.Keys;

    /// <summary>
    /// Throws an exception if this is an error response.
    /// </summary>
    public void ThrowIfError()
    {
        if (IsError)
        {
            throw new BlueBuzzahCommandException(ErrorMessage ?? "Unknown error");
        }
    }
}

/// <summary>
/// Exception thrown when a BlueBuzzah command returns an error response.
/// </summary>
public class BlueBuzzahCommandException : Exception
{
    public BlueBuzzahCommandException(string message) : base(message)
    {
    }

    public BlueBuzzahCommandException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
