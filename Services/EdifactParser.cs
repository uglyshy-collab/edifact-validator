using EdifactValidator.Models;
using System.Text;

namespace EdifactValidator.Services;

public static class EdifactParser
{
    public static EdifactInterchange Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Datei ist leer.");

        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        var (delimiters, offset) = DetectDelimiters(text);
        var body = text[offset..];

        var rawSegments = SplitIntoRawSegments(body, delimiters.SegmentTerminator, delimiters.ReleaseCharacter);

        var segments = rawSegments
            .Select((r, i) => ParseSegment(r.Raw, i, r.LineNumber, delimiters))
            .Where(s => !string.IsNullOrWhiteSpace(s.Tag))
            .ToList();

        var interchange = new EdifactInterchange
        {
            Delimiters  = delimiters,
            AllSegments = segments,
        };

        interchange.Unb = segments.FirstOrDefault(s => s.Tag == "UNB");
        interchange.Unz = segments.LastOrDefault(s => s.Tag == "UNZ");
        interchange.Messages.AddRange(GroupMessages(segments));

        return interchange;
    }

    // ── Delimiter detection ──────────────────────────────────────────────────

    private static (EdifactDelimiters, int offset) DetectDelimiters(string text)
    {
        if (text.Length >= 9 && text.StartsWith("UNA"))
            return (EdifactDelimiters.FromUna(text[3..9]), 9);
        return (EdifactDelimiters.Default, 0);
    }

    // ── Segment splitting ────────────────────────────────────────────────────

    private static List<(string Raw, int LineNumber)> SplitIntoRawSegments(
        string body, char segTerm, char releaseChar)
    {
        var results    = new List<(string, int)>();
        var buffer     = new StringBuilder();
        var lineNumber = 1;
        var segStart   = 1;
        var i          = 0;

        while (i < body.Length)
        {
            var c = body[i];
            if (c == releaseChar && i + 1 < body.Length)
            {
                buffer.Append(body[i + 1]);
                if (body[i + 1] == '\n') lineNumber++;
                i += 2;
                continue;
            }
            if (c == '\n') lineNumber++;
            if (c == segTerm)
            {
                var raw = buffer.ToString().Trim();
                if (raw.Length > 0)
                    results.Add((raw, segStart));
                buffer.Clear();
                segStart = lineNumber;
            }
            else
            {
                buffer.Append(c);
            }
            i++;
        }
        var last = buffer.ToString().Trim();
        if (last.Length > 0) results.Add((last, segStart));
        return results;
    }

    // ── Segment parsing ──────────────────────────────────────────────────────

    private static EdifactSegment ParseSegment(
        string raw, int index, int lineNumber, EdifactDelimiters d)
    {
        var elemParts = SplitOn(raw, d.ElementSeparator, d.ReleaseCharacter);
        var elements  = elemParts
            .Select(e => SplitOn(e, d.ComponentSeparator, d.ReleaseCharacter))
            .ToArray();

        return new EdifactSegment
        {
            Tag          = elements.Length > 0 && elements[0].Length > 0 ? elements[0][0].Trim() : string.Empty,
            SegmentIndex = index,
            LineNumber   = lineNumber,
            Elements     = elements,
        };
    }

    // ── Release-character-aware split ────────────────────────────────────────

    internal static string[] SplitOn(string input, char delimiter, char releaseChar)
    {
        var results = new List<string>();
        var buffer  = new StringBuilder();
        var i       = 0;

        while (i < input.Length)
        {
            var c = input[i];
            if (c == releaseChar && i + 1 < input.Length)
            {
                buffer.Append(input[i + 1]);
                i += 2;
                continue;
            }
            if (c == delimiter)
            {
                results.Add(buffer.ToString());
                buffer.Clear();
            }
            else
            {
                buffer.Append(c);
            }
            i++;
        }
        results.Add(buffer.ToString());
        return results.ToArray();
    }

    // ── Message grouping ─────────────────────────────────────────────────────

    private static List<EdifactMessage> GroupMessages(List<EdifactSegment> segments)
    {
        var messages = new List<EdifactMessage>();
        EdifactMessage? current = null;

        foreach (var seg in segments)
        {
            switch (seg.Tag)
            {
                case "UNH":
                    current = new EdifactMessage { Unh = seg };
                    break;
                case "UNT":
                    if (current != null)
                    {
                        current.Unt = seg;
                        messages.Add(current);
                        current = null;
                    }
                    break;
                default:
                    if (current != null && seg.Tag is not "UNB" and not "UNZ")
                        current.Segments.Add(seg);
                    break;
            }
        }

        // Unclosed message (missing UNT)
        if (current != null) messages.Add(current);

        return messages;
    }
}
