using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace ScreenMind.UI.Chat;

/// <summary>Small, dependency-free Markdown renderer tailored for chat responses.</summary>
internal sealed class MarkdownMessageView : StackPanel
{
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#E8ECF7"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#9AA4BC"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#A7B4FF"));
    // Semi-transparent so streaming markdown does not paint the chat solid.
    private static readonly IBrush CodeBackground = new SolidColorBrush(Color.FromArgb(120, 11, 16, 32));
    private static readonly IBrush CodeBorder = new SolidColorBrush(Color.FromArgb(100, 41, 50, 77));
    private static readonly IBrush QuoteBackground = new SolidColorBrush(Color.FromArgb(70, 21, 28, 49));

    public MarkdownMessageView(string markdown)
    {
        Spacing = 9;
        Render(markdown ?? string.Empty);
    }

    private void Render(string markdown)
    {
        string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        List<string> paragraph = [];
        List<string> code = [];
        bool inCode = false;
        string language = string.Empty;

        foreach (string line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode) { AddCodeBlock(language, code); code.Clear(); }
                else { FlushParagraph(paragraph); language = line[3..].Trim(); }
                inCode = !inCode;
                continue;
            }

            if (inCode) { code.Add(line); continue; }
            if (string.IsNullOrWhiteSpace(line)) { FlushParagraph(paragraph); continue; }
            if (TryAddStructuredLine(line)) { FlushParagraph(paragraph); continue; }
            paragraph.Add(line.Trim());
        }

        if (code.Count > 0) AddCodeBlock(language, code);
        FlushParagraph(paragraph);
    }

    private bool TryAddStructuredLine(string line)
    {
        string trimmed = line.TrimStart();
        int level = trimmed.TakeWhile(character => character == '#').Count();
        if (level is >= 1 and <= 4 && trimmed.Length > level && trimmed[level] == ' ')
        {
            Children.Add(CreateRichText(trimmed[(level + 1)..], 22 - level * 2, FontWeight.SemiBold));
            return true;
        }

        if (trimmed is "---" or "***" or "___")
        {
            Children.Add(new Border { Height = 1, Background = CodeBorder, Margin = new Thickness(0, 5) });
            return true;
        }

        if (trimmed.StartsWith("- [ ] ", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase))
        {
            bool complete = char.ToLowerInvariant(trimmed[3]) == 'x';
            Children.Add(CreateRichText($"{(complete ? "☑" : "☐")}  {trimmed[6..]}", 13, FontWeight.Normal));
            return true;
        }

        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            Children.Add(CreateRichText($"•  {trimmed[2..]}", 13, FontWeight.Normal));
            return true;
        }

        int dot = trimmed.IndexOf(". ", StringComparison.Ordinal);
        if (dot > 0 && trimmed[..dot].All(char.IsDigit))
        {
            Children.Add(CreateRichText(trimmed, 13, FontWeight.Normal));
            return true;
        }

        if (trimmed.StartsWith("> ", StringComparison.Ordinal))
        {
            Children.Add(new Border
            {
                Background = QuoteBackground,
                BorderBrush = AccentBrush,
                BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(12, 8),
                Child = CreateRichText(trimmed[2..], 13, FontWeight.Normal, MutedBrush),
            });
            return true;
        }
        return false;
    }

    private void FlushParagraph(List<string> paragraph)
    {
        if (paragraph.Count == 0) return;
        Children.Add(CreateRichText(string.Join(' ', paragraph), 13, FontWeight.Normal));
        paragraph.Clear();
    }

    private void AddCodeBlock(string language, List<string> lines)
    {
        string source = string.Join(Environment.NewLine, lines);
        Grid header = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(language) ? "CODE" : language.ToUpperInvariant(),
            FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Button copy = new()
        {
            Content = "Copy", FontSize = 10, Foreground = AccentBrush, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(8, 3), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        copy.Click += async (_, _) =>
        {
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is null) return;
            await top.Clipboard.SetTextAsync(source);
            copy.Content = "Copied";
        };
        Grid.SetColumn(copy, 1); header.Children.Add(copy);

        StackPanel content = new() { Spacing = 8 };
        content.Children.Add(header);
        content.Children.Add(new SelectableTextBlock
        {
            Text = source, FontFamily = FontFamily.Parse("Cascadia Mono, Consolas"), FontSize = 12,
            Foreground = TextBrush, TextWrapping = TextWrapping.Wrap, LineHeight = 18,
        });
        Children.Add(new Border
        {
            Background = CodeBackground, BorderBrush = CodeBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(13, 9), Child = content,
        });
    }

    private static TextBlock CreateRichText(string text, double size, FontWeight weight, IBrush? foreground = null)
    {
        TextBlock block = new()
        {
            FontSize = size, FontWeight = weight, Foreground = foreground ?? TextBrush,
            TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.5,
        };
        AddInlineContent(block.Inlines!, text);
        return block;
    }

    private static void AddInlineContent(InlineCollection inlines, string text)
    {
        int cursor = 0;
        while (cursor < text.Length)
        {
            int next = FindNextMarker(text, cursor);
            if (next < 0) { inlines.Add(new Run(text[cursor..])); break; }
            if (next > cursor) inlines.Add(new Run(text[cursor..next]));

            string marker = text[next] == '`' ? "`" : text.Substring(next, Math.Min(2, text.Length - next));
            string close = marker is "**" or "__" or "~~" ? marker : marker[0].ToString();
            int end = text.IndexOf(close, next + close.Length, StringComparison.Ordinal);
            if (end < 0) { inlines.Add(new Run(text[next..])); break; }
            string value = text.Substring(next + close.Length, end - next - close.Length);
            Inline inline;
            if (close is "**" or "__")
            {
                Bold bold = new();
                bold.Inlines.Add(new Run(value));
                inline = bold;
            }
            else if (close is "*" or "_")
            {
                Italic italic = new();
                italic.Inlines.Add(new Run(value));
                inline = italic;
            }
            else if (close == "~~")
            {
                inline = new Run(value) { TextDecorations = TextDecorations.Strikethrough };
            }
            else if (close == "`")
            {
                inline = new Run(value) { FontFamily = FontFamily.Parse("Cascadia Mono, Consolas"), Foreground = AccentBrush };
            }
            else inline = new Run(value);
            inlines.Add(inline);
            cursor = end + close.Length;
        }
    }

    private static int FindNextMarker(string text, int start)
    {
        int best = -1;
        foreach (string marker in new[] { "**", "__", "~~", "`", "*", "_" })
        {
            int found = text.IndexOf(marker, start, StringComparison.Ordinal);
            if (found >= 0 && (best < 0 || found < best)) best = found;
        }
        return best;
    }
}
