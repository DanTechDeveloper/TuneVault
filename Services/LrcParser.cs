using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using TuneVault.Models;

namespace TuneVault.Services;

/// <summary>
/// Parses the loosely standardized LRC format. Supports [mm:ss.cc], [mm:ss:cc],
/// [mm:ss.ttt], [mm:ss], multiple stamps per line, [offset:], and strips the
/// enhanced-LRC word tags we do not render.
/// </summary>
public static class LrcParser
{
    private static readonly Regex Timestamp = new(
        @"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WordTag = new(
        @"<\d{1,3}:\d{2}(?:[.:]\d{1,3})?>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MetaTag = new(
        @"^\[[a-zA-Z][a-zA-Z0-9]*:[^\]]*\]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex OffsetTag = new(
        @"^\[offset:\s*([+-]?\d+)\]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static LyricsResult Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return LyricsResult.Empty;
        }

        var offset = TimeSpan.Zero;
        var parsed = new List<(TimeSpan Time, string Text)>();
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var offsetMatch = OffsetTag.Match(line);
            if (offsetMatch.Success)
            {
                if (int.TryParse(offsetMatch.Groups[1].Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var milliseconds))
                {
                    offset = TimeSpan.FromMilliseconds(milliseconds);
                }
                continue;
            }

            if (MetaTag.IsMatch(line))
            {
                continue;
            }

            var stamps = Timestamp.Matches(line);
            if (stamps.Count == 0)
            {
                continue;
            }

            var display = WordTag.Replace(Timestamp.Replace(line, ""), "").Trim();
            foreach (Match stamp in stamps)
            {
                parsed.Add((ParseTime(stamp) + offset, display));
            }
        }

        if (parsed.Count > 0)
        {
            var ordered = parsed.OrderBy(p => p.Time)
                .Select(p => new LyricLine(p.Time, p.Text))
                .ToArray();
            return new LyricsResult(ordered, true, "");
        }

        // No timestamps: keep the words as one static block (Poweramp behavior).
        var plain = string.Join("\n", lines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !MetaTag.IsMatch(l) && !OffsetTag.IsMatch(l))).Trim();
        return plain.Length == 0
            ? LyricsResult.Empty
            : new LyricsResult(new[] { new LyricLine(TimeSpan.Zero, plain) }, false, "");
    }

    private static TimeSpan ParseTime(Match stamp)
    {
        var minutes = int.Parse(stamp.Groups[1].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(stamp.Groups[2].Value, CultureInfo.InvariantCulture);
        var fractionPart = stamp.Groups[3].Value;
        var fraction = fractionPart.Length == 0
            ? 0
            : double.Parse(fractionPart, CultureInfo.InvariantCulture) / Math.Pow(10, fractionPart.Length);
        return TimeSpan.FromSeconds(minutes * 60 + seconds + fraction);
    }
}
