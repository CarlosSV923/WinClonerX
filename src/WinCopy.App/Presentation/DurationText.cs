using WinCopy.App.Localization;

namespace WinCopy.App.Presentation;

/// <summary>Formats an estimated duration for people, rounding up to whole minutes.</summary>
public static class DurationText
{
    public static string Format(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
        {
            return Loc.Get("Review_Duration_UnderMinute");
        }

        var minutes = (int)Math.Ceiling(duration.TotalMinutes);
        if (minutes >= 60)
        {
            return Loc.Format("Review_Duration_Hours", minutes / 60, minutes % 60);
        }

        return Loc.Format("Review_Duration_Minutes", minutes);
    }
}
