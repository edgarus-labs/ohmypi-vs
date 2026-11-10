using OhMyPi.VisualStudio.UI.Model;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// The composer's prompt: plain text in a rich text box, so the command word of a slash-command prompt
/// (<see cref="SlashCommands.CommandSpan"/>) is drawn bold. <see cref="Text"/> and <see cref="CaretIndex"/> are plain
/// text with "\n" line breaks; pasted and dropped content arrives as plain text and the formatting commands
/// (Ctrl+B and the like) do nothing, so the bold command word is the only formatting the prompt ever holds.
/// </summary>
internal sealed class PromptBox : RichTextBox
{
    private static readonly ICommand[] FormattingCommands =
    [
        EditingCommands.ToggleBold, EditingCommands.ToggleItalic, EditingCommands.ToggleUnderline,
        EditingCommands.ToggleSubscript, EditingCommands.ToggleSuperscript,
        EditingCommands.IncreaseFontSize, EditingCommands.DecreaseFontSize,
        EditingCommands.AlignLeft, EditingCommands.AlignCenter, EditingCommands.AlignRight, EditingCommands.AlignJustify,
        EditingCommands.ToggleBullets, EditingCommands.ToggleNumbering,
        EditingCommands.IncreaseIndentation, EditingCommands.DecreaseIndentation,
    ];

    /// <summary>Room a rich text box always keeps between its padding and the text, left and right, so the caret is not clipped at the edges.</summary>
    public const double CaretRoom = 5;

    private bool _emboldening;

    public PromptBox()
    {
        Document = new FlowDocument(new Paragraph());
        Resources.Add(typeof(Paragraph), new Style(typeof(Paragraph)) { Setters = { new Setter(Block.MarginProperty, new Thickness(0)) } });
        AcceptsTab = false;
        DataObject.AddPastingHandler(this, OnPasting);
        CommandManager.AddPreviewCanExecuteHandler(this, OnCanExecute);
        CommandManager.AddPreviewExecutedHandler(this, OnExecuted);
        TextChanged += (_, e) =>
        {
            if (!_emboldening && e.UndoAction != UndoAction.Undo && e.UndoAction != UndoAction.Redo)
            {
                Embolden();
            }
        };
    }

    /// <summary>The prompt as plain text: paragraphs and line breaks are "\n".</summary>
    public string Text
    {
        get
        {
            var text = new TextRange(Document.ContentStart, Document.ContentEnd).Text.Replace("\r\n", "\n");

            return text.EndsWith("\n", StringComparison.Ordinal) ? text.Substring(0, text.Length - 1) : text;
        }
        set
        {
            var paragraph = new Paragraph();
            var lines = (value ?? "").Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    paragraph.Inlines.Add(new LineBreak());
                }

                if (lines[i].Length > 0)
                {
                    paragraph.Inlines.Add(new Run(lines[i]));
                }
            }

            BeginChange();
            try
            {
                Document.Blocks.Clear();
                Document.Blocks.Add(paragraph);
                Embolden();
            }
            finally
            {
                EndChange();
            }
        }
    }

    /// <summary>The caret as an index into <see cref="Text"/>.</summary>
    public int CaretIndex
    {
        get => new TextRange(Document.ContentStart, CaretPosition).Text.Replace("\r\n", "\n").Length;
        set => CaretPosition = PositionAt(value);
    }

    public void Clear() => Text = "";

    /// <summary>The caret rectangle before the character at <paramref name="index"/> in <see cref="Text"/>.</summary>
    public Rect GetRectFromCharacterIndex(int index) => PositionAt(index).GetCharacterRect(LogicalDirection.Forward);

    /// <summary>Makes the command word, and nothing else, bold; leaves the document alone when it already is, so typing makes no extra undo steps.</summary>
    private void Embolden()
    {
        var (start, length) = SlashCommands.CommandSpan(Text);
        if (IsEmboldened(start, length))
        {
            return;
        }

        _emboldening = true;
        BeginChange();
        try
        {
            new TextRange(Document.ContentStart, Document.ContentEnd).ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            if (length > 0)
            {
                new TextRange(PositionAt(start), PositionAt(start + length)).ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
            }
        }
        finally
        {
            EndChange();
            _emboldening = false;
        }
    }

    /// <summary>Whether exactly the characters [<paramref name="start"/>, <paramref name="start"/> + <paramref name="length"/>) are bold.</summary>
    private bool IsEmboldened(int start, int length)
    {
        var offset = 0;
        bool Check(InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Run run when run.Text.Length > 0:
                        var end = offset + run.Text.Length;
                        var bold = run.FontWeight == FontWeights.Bold;
                        var inside = offset >= start && end <= start + length;
                        var outside = end <= start || offset >= start + length;
                        if (!(bold ? inside : outside))
                        {
                            return false;
                        }

                        offset = end;
                        break;
                    case LineBreak:
                        offset++;
                        break;
                    case Span span:
                        if (!Check(span.Inlines))
                        {
                            return false;
                        }

                        break;
                }
            }

            return true;
        }

        foreach (var block in Document.Blocks)
        {
            if (block is not Paragraph paragraph || !Check(paragraph.Inlines))
            {
                return false;
            }

            offset++;
        }

        return true;
    }

    /// <summary>The position <paramref name="offset"/> characters of <see cref="Text"/> into the document, clamped to its end.</summary>
    private TextPointer PositionAt(int offset)
    {
        var position = Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        for (var i = 0; i < offset; i++)
        {
            var next = position.GetNextInsertionPosition(LogicalDirection.Forward);
            if (next is null)
            {
                break;
            }

            position = next;
        }

        return position;
    }

    /// <summary>Takes pasted and dropped content as plain text; content with no text is not pasted into the prompt.</summary>
    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            e.FormatToApply = DataFormats.UnicodeText;
        }
        else if (e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.FormatToApply = DataFormats.Text;
        }
        else
        {
            e.CancelCommand();
        }
    }

    private static void OnCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (Array.IndexOf(FormattingCommands, e.Command) >= 0)
        {
            e.CanExecute = false;
            e.Handled = true;
        }
    }

    private static void OnExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (Array.IndexOf(FormattingCommands, e.Command) >= 0)
        {
            e.Handled = true;
        }
    }
}
