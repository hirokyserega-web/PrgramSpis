using System;
using System.Collections.Generic;
using System.Text;

namespace ScreenMind.Core.Ai;

public sealed class StreamingAnswerFilter
{
    private static readonly string[] ReasoningTags = ["think", "thought", "analysis", "reasoning"];
    private readonly StringBuilder rawText = new();
    private readonly StringBuilder answerText = new();
    private readonly StringBuilder pending = new();
    private readonly StringBuilder hidden = new();
    private string? activeTag;
    private bool completed;

    private readonly bool suppressUntaggedReasoning;
    private readonly StringBuilder untaggedBuffer = new();
    private bool markerFound;

    private static readonly string[] FinalMarkers = [
        "Final Answer:",
        "Final:",
        "Окончательный ответ:",
        "Выполнил поиск в интернете",
        "Поиск в интернете",
        "Источники прочитаны",
        "Просмотрено страниц:",
        "Выполнен поиск",
        "Поиск по сайтам:",
        "Получено ответов:",
        "Ответ:"
    ];

    public StreamingAnswerFilter(bool suppressUntaggedReasoning = false)
    {
        this.suppressUntaggedReasoning = suppressUntaggedReasoning;
    }

    public string RawText => rawText.ToString();
    public string AnswerText => answerText.ToString();
    public bool HasExplicitAnswer => answerText.Length > 0;
    public bool HasUnresolvedReasoning => completed && answerText.Length == 0;
    public bool IsReasoningOnly => completed && answerText.Length == 0;

    public string Push(string chunk)
    {
        if (completed || string.IsNullOrEmpty(chunk))
        {
            return string.Empty;
        }

        rawText.Append(chunk);
        pending.Append(chunk);
        
        string xmlDrained = DrainXmlTags(final: false);
        return ProcessUntagged(xmlDrained, final: false);
    }

    public string Complete()
    {
        if (completed)
        {
            return string.Empty;
        }

        string xmlDrained = DrainXmlTags(final: true);
        string visible = ProcessUntagged(xmlDrained, final: true);
        completed = true;
        return visible;
    }

    private string ProcessUntagged(string text, bool final)
    {
        if (!suppressUntaggedReasoning)
        {
            answerText.Append(text);
            return text;
        }

        if (markerFound)
        {
            answerText.Append(text);
            return text;
        }

        untaggedBuffer.Append(text);
        string bufferText = untaggedBuffer.ToString();

        foreach (string marker in FinalMarkers)
        {
            int index = bufferText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                markerFound = true;
                string remaining = bufferText[(index + marker.Length)..].TrimStart('\r', '\n', ' ');
                untaggedBuffer.Clear();
                answerText.Append(remaining);
                return remaining;
            }
        }

        int russianLineStart = FindRussianLineStart(bufferText);
        if (russianLineStart >= 0)
        {
            markerFound = true;
            string remaining = bufferText[russianLineStart..].TrimStart('\r', '\n', ' ');
            untaggedBuffer.Clear();
            answerText.Append(remaining);
            return remaining;
        }

        if (final)
        {
            string remaining = untaggedBuffer.ToString();
            untaggedBuffer.Clear();

            // If the buffer is short (under 60 chars), it's a short answer (e.g. number or code), not long reasoning.
            if (remaining.Length < 60)
            {
                answerText.Append(remaining);
                return remaining;
            }

            if (FindRussianLineStart(remaining) < 0)
            {
                return string.Empty;
            }

            answerText.Append(remaining);
            return remaining;
        }

        return string.Empty;
    }

    private string DrainXmlTags(bool final)
    {
        StringBuilder visible = new();
        while (true)
        {
            if (activeTag is not null)
            {
                string closeTag = CloseTag(activeTag);
                string combined = hidden.ToString() + pending;
                int closeStart = combined.IndexOf(closeTag, StringComparison.OrdinalIgnoreCase);
                if (closeStart < 0)
                {
                    if (final)
                    {
                        visible.Append(rawText);
                        hidden.Clear();
                        pending.Clear();
                        activeTag = null;
                    }
                    else
                    {
                        int suffixLength = SuffixLength(combined, closeTag);
                        int safeLength = combined.Length - suffixLength;
                        hidden.Clear();
                        if (safeLength > 0)
                        {
                            hidden.Append(combined[..safeLength]);
                        }
                        pending.Clear();
                        if (suffixLength > 0)
                        {
                            pending.Append(combined[safeLength..]);
                        }
                    }
                    break;
                }

                hidden.Clear();
                pending.Clear();
                activeTag = null;
                if (closeStart + closeTag.Length < combined.Length)
                {
                    pending.Append(combined[(closeStart + closeTag.Length)..]);
                }
                continue;
            }

            int openingStart = FindOpening(pending, out string? tag, out int openingLength);
            if (openingStart >= 0 && tag is not null)
            {
                if (openingStart > 0)
                {
                    visible.Append(pending.ToString(0, openingStart));
                }

                hidden.Clear();
                hidden.Append(pending.ToString(0, openingStart + openingLength));
                pending.Remove(0, openingStart + openingLength);
                activeTag = tag;
                continue;
            }

            if (!final)
            {
                int suffixLength = OpeningPrefixLength(pending);
                int emitLength = pending.Length - suffixLength;
                if (emitLength > 0)
                {
                    visible.Append(pending.ToString(0, emitLength));
                    pending.Remove(0, emitLength);
                }
                break;
            }

            visible.Append(pending);
            pending.Clear();
            break;
        }

        return visible.ToString();
    }

    private static int FindOpening(StringBuilder value, out string? tag, out int length)
    {
        tag = null;
        length = 0;
        int best = -1;
        foreach (string candidate in ReasoningTags)
        {
            int index = value.ToString().IndexOf($"<{candidate}>", StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (best < 0 || index < best))
            {
                best = index;
                tag = candidate;
                length = candidate.Length + 2;
            }
        }
        return best;
    }

    private static int IndexOfClose(StringBuilder value, string tag)
        => value.ToString().IndexOf(CloseTag(tag), StringComparison.OrdinalIgnoreCase);

    private static string CloseTag(string tag) => $"</{tag}>";

    private static int OpeningPrefixLength(StringBuilder value)
    {
        string text = value.ToString();
        int best = 0;
        foreach (string tag in ReasoningTags)
        {
            string opening = $"<{tag}>";
            for (int length = 1; length < opening.Length; length++)
            {
                if (text.EndsWith(opening[..length], StringComparison.OrdinalIgnoreCase))
                {
                    best = Math.Max(best, length);
                }
            }
        }
        return best;
    }

    private static int SuffixLength(string value, string token)
    {
        int best = 0;
        for (int k = 1; k < token.Length; k++)
        {
            if (value.EndsWith(token[..k], StringComparison.OrdinalIgnoreCase))
            {
                best = k;
            }
        }
        return best;
    }

    private static int FindRussianLineStart(string text)
    {
        int lineStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' || i == text.Length - 1)
            {
                int lineEnd = (text[i] == '\n') ? i : i + 1;
                string line = text[lineStart..lineEnd];

                int cyrillicCount = 0;
                int latinCount = 0;
                foreach (char c in line)
                {
                    if (c >= '\u0400' && c <= '\u04FF') cyrillicCount++;
                    else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latinCount++;
                }

                if (cyrillicCount >= 5 && cyrillicCount > latinCount)
                {
                    return lineStart;
                }

                lineStart = i + 1;
            }
        }
        return -1;
    }
}
