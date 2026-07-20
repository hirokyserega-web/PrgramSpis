using System;
using System.Text;

namespace ScreenMind.Core.Ai;

public sealed class StreamingAnswerFilter
{
    private enum FilterState
    {
        Unknown,
        Reasoning,
        Answer,
        Completed,
    }

    private static readonly string[] ReasoningTags = ["think", "thought", "analysis", "reasoning"];
    private static readonly string[] AnswerMarkers =
    [
        "**Final Answer:**",
        "Final Answer:",
        "**Final:**",
        "Final:",
        "Ответ:",
        "Итоговый ответ:",
        "Окончательный ответ:",
        "Финal Answer:",
    ];
    private static readonly string[] ReasoningStarters =
    [
        "Пользователь просит",
        "Пользователь спрашивает",
        "Пользователь хочет",
        "Мне нужно",
        "Нужно проанализировать",
        "Нужно ответить",
        "The user asks",
        "The user wants",
        "The user requests",
        "I need to",
        "We need to",
        "Let me analyze",
        "Let's analyze",
    ];

    private readonly bool suppressUntaggedReasoning;
    private readonly StringBuilder rawText = new();
    private readonly StringBuilder answerText = new();
    private readonly StringBuilder pending = new();
    private readonly StringBuilder reasoningText = new();
    private FilterState state;
    private string? activeReasoningTag;
    private bool insideFinalTag;
    private bool sawTaggedReasoning;
    private bool sawUntaggedReasoning;
    private bool hasExplicitAnswer;
    private bool unresolvedReasoning;
    private bool completed;

    public StreamingAnswerFilter(bool suppressUntaggedReasoning = false)
    {
        this.suppressUntaggedReasoning = suppressUntaggedReasoning;
    }

    public string RawText => rawText.ToString();
    public string AnswerText => answerText.ToString();
    public bool HasExplicitAnswer => hasExplicitAnswer;
    public bool HasUnresolvedReasoning => unresolvedReasoning;
    public bool IsReasoningOnly => unresolvedReasoning && (sawTaggedReasoning || sawUntaggedReasoning) && answerText.Length == 0;

    public string Push(string chunk)
    {
        if (completed || string.IsNullOrEmpty(chunk))
        {
            return string.Empty;
        }

        rawText.Append(chunk);
        pending.Append(chunk);
        return Drain(final: false);
    }

    public string Complete()
    {
        if (completed)
        {
            return string.Empty;
        }

        string visible = Drain(final: true);
        completed = true;
        state = FilterState.Completed;
        return visible;
    }

    private string Drain(bool final)
    {
        StringBuilder visible = new();
        bool progress;
        do
        {
            progress = false;
            if (state == FilterState.Unknown)
            {
                string value = pending.ToString();
                int reasoningStart = FindReasoningOpening(value, out string? reasoningTag);
                int finalStart = value.IndexOf("<final>", StringComparison.OrdinalIgnoreCase);
                int markerStart = FindAnswerMarker(value, out int markerLength);

                if (reasoningStart >= 0 && (finalStart < 0 || reasoningStart < finalStart)
                    && (markerStart < 0 || reasoningStart < markerStart))
                {
                    RemovePrefix(reasoningStart + OpeningTagLength(value, reasoningStart));
                    activeReasoningTag = reasoningTag;
                    sawTaggedReasoning = true;
                    state = FilterState.Reasoning;
                    progress = true;
                    continue;
                }

                if (finalStart >= 0 && (markerStart < 0 || finalStart < markerStart))
                {
                    RemovePrefix(finalStart + "<final>".Length);
                    TrimLeadingSeparators();
                    insideFinalTag = true;
                    hasExplicitAnswer = true;
                    state = FilterState.Answer;
                    progress = true;
                    continue;
                }

                if (markerStart >= 0)
                {
                    RemovePrefix(markerStart + markerLength);
                    TrimLeadingSeparators();
                    hasExplicitAnswer = true;
                    state = FilterState.Answer;
                    progress = true;
                    continue;
                }

                if (!final && HasPartialTokenSuffix(value))
                {
                    break;
                }

                if (suppressUntaggedReasoning && IsReasoningStartOrPrefix(value))
                {
                    sawUntaggedReasoning = true;
                    reasoningText.Append(pending);
                    pending.Clear();
                    state = FilterState.Reasoning;
                    progress = true;
                    continue;
                }

                state = FilterState.Answer;
                progress = true;
                continue;
            }

            if (state == FilterState.Reasoning)
            {
                if (activeReasoningTag is not null)
                {
                    string closeTag = $"</{activeReasoningTag}>";
                    int closeStart = pending.ToString().IndexOf(closeTag, StringComparison.OrdinalIgnoreCase);
                    if (closeStart < 0)
                    {
                        if (pending.Length > 0)
                        {
                            reasoningText.Append(pending);
                            pending.Clear();
                        }
                        if (final)
                        {
                            unresolvedReasoning = true;
                        }
                        else
                        {
                            string closePrefix = $"</{activeReasoningTag}>";
                            int suffixLength = SuffixLength(reasoningText.ToString(), closePrefix);
                            if (suffixLength > 0)
                            {
                                reasoningText.Remove(reasoningText.Length - suffixLength, suffixLength);
                                pending.Append(closePrefix[..suffixLength]);
                            }
                        }
                        break;
                    }

                    if (closeStart > 0)
                    {
                        reasoningText.Append(pending.ToString(0, closeStart));
                    }
                    RemovePrefix(closeStart + closeTag.Length);
                    activeReasoningTag = null;
                    state = FilterState.Answer;
                    progress = true;
                    continue;
                }

                if (pending.Length > 0)
                {
                    reasoningText.Append(pending);
                    pending.Clear();
                }

                string reasoning = reasoningText.ToString();
                int finalStart = reasoning.IndexOf("<final>", StringComparison.OrdinalIgnoreCase);
                int markerStart = FindAnswerMarker(reasoning, out int markerLength);
                if (finalStart >= 0 && (markerStart < 0 || finalStart < markerStart))
                {
                    MoveReasoningRemainderToPending(reasoning, finalStart + "<final>".Length);
                    TrimLeadingSeparators();
                    insideFinalTag = true;
                    hasExplicitAnswer = true;
                    state = FilterState.Answer;
                    progress = true;
                    continue;
                }

                if (markerStart >= 0)
                {
                    MoveReasoningRemainderToPending(reasoning, markerStart + markerLength);
                    TrimLeadingSeparators();
                    hasExplicitAnswer = true;
                    state = FilterState.Answer;
                    progress = true;
                    continue;
                }

                if (final)
                {
                    unresolvedReasoning = true;
                }
                break;
            }

            if (state == FilterState.Answer)
            {
                if (insideFinalTag)
                {
                    int closeStart = pending.ToString().IndexOf("</final>", StringComparison.OrdinalIgnoreCase);
                    if (closeStart >= 0)
                    {
                        EmitPrefix(pending, closeStart, visible);
                        RemovePrefix(closeStart + "</final>".Length);
                        insideFinalTag = false;
                        progress = true;
                        continue;
                    }

                    if (!final)
                    {
                        int suffixLength = SuffixLength(pending.ToString(), "</final>");
                        if (suffixLength > 0)
                        {
                            EmitPrefix(pending, pending.Length - suffixLength, visible);
                            RemoveSuffix(suffixLength);
                            break;
                        }
                    }
                }

                int reasoningStart = FindReasoningOpening(pending.ToString(), out string? reasoningTag);
                if (reasoningStart >= 0 && reasoningTag is not null)
                {
                    EmitPrefix(pending, reasoningStart, visible);
                    RemovePrefix(reasoningStart + OpeningTagLength(pending.ToString(), reasoningStart));
                    activeReasoningTag = reasoningTag;
                    sawTaggedReasoning = true;
                    state = FilterState.Reasoning;
                    progress = true;
                    continue;
                }

                if (!final)
                {
                    int suffixLength = OpeningSuffixLength(pending.ToString());
                    if (suffixLength > 0)
                    {
                        EmitPrefix(pending, pending.Length - suffixLength, visible);
                        RemoveSuffix(suffixLength);
                        break;
                    }
                }

                visible.Append(pending);
                pending.Clear();
                break;
            }
        }
        while (progress);

        string result = visible.ToString();
        answerText.Append(result);

        if (final && sawUntaggedReasoning && !hasExplicitAnswer && answerText.Length == 0)
        {
            unresolvedReasoning = true;
            pending.Clear();
            reasoningText.Clear();
            result = rawText.ToString();
            answerText.Append(result);
        }

        return result;
    }

    private void MoveReasoningRemainderToPending(string reasoning, int start)
    {
        reasoningText.Clear();
        pending.Clear();
        if (start < reasoning.Length)
        {
            pending.Append(reasoning[start..]);
        }
    }

    private static bool IsReasoningStartOrPrefix(string value)
    {
        string trimmed = value.TrimStart();
        if (trimmed.Length == 0)
        {
            return false;
        }

        foreach (string starter in ReasoningStarters)
        {
            if (trimmed.StartsWith(starter, StringComparison.OrdinalIgnoreCase)
                || starter.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static int FindAnswerMarker(string value, out int markerLength)
    {
        markerLength = 0;
        int best = -1;
        foreach (string marker in AnswerMarkers)
        {
            int index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (best < 0 || index < best || index == best && marker.Length > markerLength))
            {
                best = index;
                markerLength = marker.Length;
            }
        }
        return best;
    }

    private static int FindReasoningOpening(string value, out string? tag)
    {
        tag = null;
        int best = -1;
        foreach (string candidate in ReasoningTags)
        {
            int index = value.IndexOf($"<{candidate}>", StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (best < 0 || index < best))
            {
                best = index;
                tag = candidate;
            }
        }
        return best;
    }

    private static int OpeningTagLength(string value, int start)
    {
        int end = value.IndexOf('>', start);
        return end < 0 ? 0 : end - start + 1;
    }

    private static int OpeningSuffixLength(string value)
    {
        int best = 0;
        foreach (string tag in ReasoningTags)
        {
            best = Math.Max(best, SuffixLength(value, $"<{tag}>"));
        }
        return best;
    }

    private static bool HasPartialTokenSuffix(string value)
    {
        foreach (string tag in ReasoningTags)
        {
            if (SuffixLength(value, $"<{tag}>") > 0 || SuffixLength(value, $"</{tag}>") > 0)
            {
                return true;
            }
        }

        if (SuffixLength(value, "<final>") > 0)
        {
            return true;
        }

        foreach (string marker in AnswerMarkers)
        {
            if (MarkerPrefixLength(value, marker) > 0)
            {
                return true;
            }
        }
        return false;
    }

    private static int MarkerPrefixLength(string value, string marker)
    {
        int max = Math.Min(value.Length, marker.Length - 1);
        for (int length = max; length > 0; length--)
        {
            if (marker.StartsWith(value[^length..], StringComparison.OrdinalIgnoreCase))
            {
                return length;
            }
        }
        return 0;
    }

    private static int SuffixLength(string value, string token)
    {
        int max = Math.Min(value.Length, token.Length - 1);
        for (int length = max; length > 0; length--)
        {
            if (value.EndsWith(token[..length], StringComparison.OrdinalIgnoreCase))
            {
                return length;
            }
        }
        return 0;
    }

    private void RemovePrefix(int length)
    {
        pending.Remove(0, Math.Min(length, pending.Length));
    }

    private void RemoveSuffix(int length)
    {
        pending.Remove(Math.Max(0, pending.Length - length), Math.Min(length, pending.Length));
    }

    private void TrimLeadingSeparators()
    {
        int count = 0;
        while (count < pending.Length && pending[count] is ' ' or '\r' or '\n' or '\t')
        {
            count++;
        }
        if (count > 0)
        {
            pending.Remove(0, count);
        }
    }

    private static void EmitPrefix(StringBuilder source, int length, StringBuilder destination)
    {
        if (length > 0)
        {
            destination.Append(source.ToString(0, length));
        }
    }
}
