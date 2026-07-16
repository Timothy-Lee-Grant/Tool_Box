namespace ToolBox.Basics;

/// <summary>Result shape for the current_time tool.</summary>
/// <param name="Utc">ISO-8601 round-trip format ("O"), UTC.</param>
/// <param name="Local">ISO-8601 round-trip format ("O"), server-local time with offset.</param>
/// <param name="TimeZone">IANA/OS identifier of the server's local time zone.</param>
public sealed record CurrentTime(string Utc, string Local, string TimeZone);
