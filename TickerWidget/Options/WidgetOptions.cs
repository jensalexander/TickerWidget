using System;
using System.ComponentModel.DataAnnotations;

namespace TickerWidget.Options;


public sealed class WidgetOptions
{
    // Kan parses direkte fra "HH:mm:ss" eller "mm:ss"
    [Required]
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(2);

    [Required]
    public ActiveHoursOptions ActiveHours { get; init; } = new();
}

public sealed class ActiveHoursOptions
{
    // "09:00" -> TimeSpan(9,0,0)
    [Required]
    public TimeSpan Start { get; init; } = new(9, 0, 0);

    [Required]
    public TimeSpan End { get; init; } = new(17, 0, 0);

    public bool IsWithin(DateTimeOffset now)
    {
        var t = now.TimeOfDay;
        // simple dag-interval (uden over-midnat). Udvid evt. hvis End < Start.
        return t >= Start && t < End;
    }
}
