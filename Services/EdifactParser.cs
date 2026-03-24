using EdifactValidator.Models;
using System.Text;

namespace EdifactValidator.Services;

public static class EdifactParser
{
    public static EdifactInterchange Parse(string text)
    {
        var all = ParseAll(text);
        return all.Count == 1 ? all[0] : MergeInterchanges(all);
    }

    /// <summary>Parst einen Text mit einem oder mehreren UNB…UNZ Interchanges.</summary>
    public static List<EdifactInterchange> ParseAll(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Datei ist leer.");

        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        var (delimiters, offset) = DetectDelimiters(text);
        var body = text[offset..];

        var rawSegments = SplitIntoRawSegments(body, delimiters.SegmentTerminator, delimiters.ReleaseCharacter);

        var allSegments = rawSegments
            .Select((r, i) => ParseSegment(r.Raw, i, r.LineNumber, delimiters))
            .Where(s => !string.IsNullOrWhiteSpace(s.Tag))
            .ToList();

        // Slice into UNB…UNZ blocks
        var result  = new List<EdifactInterchange>();
        int blockStart = 0;

        for (int i = 0; i < allSegments.Count; i++)
        {
            if (allSegments[i].Tag == "UNZ" || i == allSegments.Count - 1)
            {
                int end = allSegments[i].Tag == "UNZ" ? i + 1 : allSegments.Count;
                var block = allSegments.GetRange(blockStart, end - blockStart);
                var ic = new EdifactInterchange
                {
                    Delimiters  = delimiters,
                    AllSegments = block,
                };
                ic.Unb = block.FirstOrDefault(s => s.Tag == "UNB");
                ic.Unz = block.FirstOrDefault(s => s.Tag == "UNZ");
                ic.Messages.AddRange(GroupMessages(block));
                result.Add(ic);
                blockStart = i + 1;
            }
        }

        return result.Count > 0 ? result
            : new List<EdifactInterchange> { BuildSingle(allSegments, delimiters) };
    }

    private static EdifactInterchange BuildSingle(List<EdifactSegment> segments, EdifactDelimiters delimiters)
    {
        var ic = new EdifactInterchange { Delimiters = delimiters, AllSegments = segments };
        ic.Unb = segments.FirstOrDefault(s => s.Tag == "UNB");
        ic.Unz = segments.LastOrDefault(s => s.Tag == "UNZ");
        ic.Messages.AddRange(GroupMessages(segments));
        return ic;
    }

    private static EdifactInterchange MergeInterchanges(List<EdifactInterchange> interchanges)
    {
        // Fallback: if caller uses Parse() on a multi-interchange file, merge everything
        var all = interchanges.SelectMany(ic => ic.AllSegments).ToList();
        return BuildSingle(all, interchanges[0].Delimiters);
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
