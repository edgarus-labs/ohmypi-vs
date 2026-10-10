using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// Prompt input card: attachment chips, the multiline input (Enter sends, Shift+Enter new line) and the toolbar
/// inside it (attach, model, effort, fast; one action button: Send when idle, Stop while the agent works).
/// While the agent works, Enter queues the prompt as a follow-up for after the turn; running subagents are
/// steered from the Agents section.
/// </summary>
internal sealed class Composer : Border
{
    private static readonly Dictionary<string, string> ImageTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
    };

    /// <summary>Distance from the card's edge to the text, the caret and the placeholder.</summary>
    private const double TextInset = 12;

    /// <summary>Room inside a 26 px icon button around its 13 px glyph, so the toolbar's first glyph aligns with the text above it.</summary>
    private const double IconInset = 6.5;

    private readonly WrapPanel _chips = new WrapPanel { Margin = new Thickness(TextInset, 6, TextInset, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _placeholder;
    private readonly TextBlock _modelText;
    private ModelView? _model;
    private bool _routerAuto;
    private readonly TextBlock _effortText;
    private readonly Button _action;
    private string? _activeDocument;
    private readonly Action<NoticeLevel, string> _notice;
    private readonly Action<string, Exception> _log;
    private readonly Dictionary<Attachment, UIElement> _chipViews = new Dictionary<Attachment, UIElement>(ByReference.Instance);
    private readonly Dictionary<Attachment, BitmapSource> _thumbnails = new Dictionary<Attachment, BitmapSource>(ByReference.Instance);
    private List<Attachment> _attachments = new List<Attachment>();
    private int _pastedSequence;
    private int _imageSequence;
    private bool _busy;
    private bool _unavailable;
    private readonly SlashCompletion _completion;

    public Composer(Action<NoticeLevel, string> notice, Action<string, Exception> log)
    {
        _notice = notice;
        _log = log;
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        this.Theme(BackgroundProperty, ThemeKeys.InputBackground);
        this.Theme(BorderBrushProperty, ThemeKeys.InputBorder);

        Input = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Padding = new Thickness(TextInset, 8, TextInset, 4),
        }.Styled("Omp.TextBox");
        Ui.AutomationName(Input, "Prompt");
        Input.PreviewKeyDown += OnInputKey;
        Input.TextChanged += (_, __) => RenderActions();
        Input.IsKeyboardFocusedChanged += (_, __) => RenderActions();
        DataObject.AddPastingHandler(Input, OnPaste);
        CommandManager.AddPreviewCanExecuteHandler(Input, OnCanPaste);
        CommandManager.AddPreviewExecutedHandler(Input, OnPasteCommand);
        Input.AllowDrop = true;
        Input.PreviewDragOver += OnDragOver;
        Input.PreviewDrop += OnDrop;

        _placeholder = Ui.Muted("", wrap: true, small: false);
        _placeholder.IsHitTestVisible = false;
        _placeholder.Margin = new Thickness(Input.Padding.Left + 1, Input.Padding.Top, Input.Padding.Right, 0);
        _placeholder.VerticalAlignment = VerticalAlignment.Top;
        var inputGrid = new Grid();
        inputGrid.SetResourceReference(TextElement.FontSizeProperty, ThemeKeys.ChatFontSize);
        inputGrid.Children.Add(Input);
        inputGrid.Children.Add(_placeholder);

        _completion = new SlashCompletion(Input);
        AttachButton = Ui.IconButton(Glyphs.Add, "Add the active editor file", () => AttachRequested?.Invoke());
        ActiveDocument = null;

        _modelText = Ui.Text("Select model", small: true);
        var modelChevron = Ui.Icon(Glyphs.ChevronDown, ThemeKeys.Muted, 8);
        modelChevron.Margin = new Thickness(4, 1, 0, 0);
        ModelButton = new Button { Content = Ui.Row(_modelText, modelChevron), MaxWidth = 200, ToolTip = "Select model" }.Styled("Omp.Button");
        Ui.AutomationName(ModelButton, "Select model");
        ModelButton.Click += (_, __) => ModelRequested?.Invoke();

        _effortText = Ui.Text("", ThemeKeys.Muted, small: true);
        var effortChevron = Ui.Icon(Glyphs.ChevronDown, ThemeKeys.Muted, 8);
        effortChevron.Margin = new Thickness(4, 1, 0, 0);
        EffortButton = new Button { Content = Ui.Row(_effortText, effortChevron), ToolTip = "Reasoning effort", Visibility = Visibility.Collapsed }.Styled("Omp.Button");
        EffortButton.Click += (_, __) => EffortRequested?.Invoke();

        FastButton = new ToggleButton { Content = Glyphs.Flash }.Styled("Omp.ToggleButton");
        Ui.AutomationName(FastButton, "Fast mode");
        FastButton.Click += (_, __) =>
        {
            var requested = FastButton.IsChecked == true;
            FastButton.IsChecked = !requested;
            FastRequested?.Invoke(requested);
        };

        _action = Ui.IconButton(Glyphs.Send, "Send", () =>
        {
            if (_busy)
            {
                StopRequested?.Invoke();
            }
            else
            {
                Submit(PromptMode.Auto);
            }
        });
        _action.Styled("Omp.SendButton");

        var left = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var element in new UIElement[] { AttachButton, ModelButton, EffortButton, FastButton })
        {
            left.Children.Add(element);
        }

        var right = Ui.Row(_action);
        right.VerticalAlignment = VerticalAlignment.Bottom;
        var toolbar = new DockPanel { Margin = new Thickness(TextInset - IconInset, 2, 8, 8) };
        DockPanel.SetDock(right, Dock.Right);
        toolbar.Children.Add(right);
        toolbar.Children.Add(left);

        var stack = new StackPanel();
        stack.Children.Add(_chips);
        stack.Children.Add(inputGrid);
        stack.Children.Add(toolbar);
        Child = stack;

        IsKeyboardFocusWithinChanged += (_, __) =>
            this.Theme(BorderBrushProperty, IsKeyboardFocusWithin ? ThemeKeys.InputBorderFocused : ThemeKeys.InputBorder);
        RenderActions();
    }

    public TextBox Input { get; }

    public Button AttachButton { get; }

    public Button ModelButton { get; }

    public Button EffortButton { get; }

    public ToggleButton FastButton { get; }

    /// <summary>Slash commands offered while the prompt starts with "/".</summary>
    public IReadOnlyList<SlashCommandView> Commands
    {
        get => _completion.Commands;
        set => _completion.Commands = value;
    }

    /// <summary>The user asked to add the active editor file.</summary>
    public event Action? AttachRequested;

    public event Action? ModelRequested;

    public event Action? EffortRequested;

    /// <summary>The user asked for fast mode on (true) or off (false).</summary>
    public event Action<bool>? FastRequested;

    public event Action? StopRequested;

    /// <summary>A prompt to send: text, attachments and mode. The composer is already cleared.</summary>
    public event Action<Draft>? Submitted;

    /// <summary>The file in the active editor; "+" adds it as an @mention and is disabled while this is null.</summary>
    public string? ActiveDocument
    {
        get => _activeDocument;
        set
        {
            _activeDocument = value;
            AttachButton.IsEnabled = value is not null;
            AttachButton.ToolTip = value is null ? "Open a file in the editor to add it to the chat" : $"Add {Path.GetFileName(value)} to the chat";
        }
    }

    public bool HasContent => Input.Text.Trim().Length > 0 || _attachments.Count > 0;

    public void SetStatus(bool busy, bool unavailable)
    {
        _busy = busy;
        _unavailable = unavailable;
        Input.IsEnabled = !unavailable;
        _placeholder.Text = busy ? "Steer the running turn…  (Enter to steer, Alt+Enter to queue a follow-up, Esc to stop)" : "Ask OMP…  (Enter to send, Shift+Enter for a new line)";
        Opacity = unavailable ? 0.6 : 1;
        RenderActions();
    }

    /// <summary>Marks the model button <c>tier auto</c> while the tier router chooses the model.</summary>
    public void SetRouterAuto(bool auto)
    {
        _routerAuto = auto;
        RenderModel();
    }

    public void RenderSession(SessionView session)
    {
        _model = session.Model;
        RenderModel();
        ModelButton.ToolTip = session.Model is not null ? $"{session.Model.Provider}/{session.Model.Id}\nClick to change the model" : "Select model";
        var effort = Effort.Label(session);
        EffortButton.Visibility = effort.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _effortText.Text = effort;
        EffortButton.ToolTip = Effort.Tooltip(session);
        Ui.AutomationName(EffortButton, $"Reasoning effort {effort}");
        var fast = session.FastModeEnabled == true;
        FastButton.IsChecked = fast;
        FastButton.ToolTip = fast ? $"Fast mode on{(session.FastModeActive == true ? " (active)" : "")}; click to turn off" : "Fast mode off; click to turn on";
    }

    private void RenderModel() => _modelText.Text = Chrome.ComposerModelText(_model, _routerAuto);

    /// <summary>Replaces the draft (OMP <c>set_editor_text</c>).</summary>
    public void SetText(string text)
    {
        Input.Text = text;
        Input.CaretIndex = text.Length;
        Input.Focus();
    }

    public void AddAttachments(IReadOnlyList<Attachment> attachments)
    {
        SetAttachments([.. Attachments.Merge(_attachments, attachments)]);
        Input.Focus();
    }

    /// <summary>Puts a draft back after OMP refused it, unless the user already typed something new.</summary>
    public void Restore(Draft draft)
    {
        if (HasContent)
        {
            return;
        }

        Input.Text = draft.Text;
        SetAttachments([.. draft.Attachments]);
    }

    private void Submit(PromptMode mode)
    {
        if (!HasContent || _unavailable)
        {
            return;
        }

        var draft = new Draft(Input.Text, _attachments, mode);
        Input.Clear();
        SetAttachments(new List<Attachment>());
        Submitted?.Invoke(draft);
    }

    private void OnInputKey(object sender, KeyEventArgs e)
    {
        if (HandleKey(e.Key, e.SystemKey, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Runs the prompt's action for a key: the slash completion's keys first, then Alt+Enter queues a follow-up and
    /// Enter sends (Shift+Enter is a new line). False when the key is left to the text box.
    /// </summary>
    internal bool HandleKey(Key key, Key systemKey, ModifierKeys modifiers)
    {
        if (_completion.HandleKey(key))
        {
            return true;
        }

        if (key == Key.System && systemKey == Key.Enter)
        {
            Submit(Chrome.SendMode(_busy, followUp: true));

            return true;
        }
        if (key != Key.Enter || (modifiers & ModifierKeys.Shift) != 0)
        {
            return false;
        }

        Submit(Chrome.SendMode(_busy, followUp: false));

        return true;
    }

    private void RenderActions()
    {
        var state = Chrome.ComposerButtons(_busy, HasContent, _unavailable);
        var label = state.IsStop ? "Stop" : "Send";
        _action.Content = state.IsStop ? Glyphs.Stop : Glyphs.Send;
        Ui.AutomationName(_action, label);
        _action.ToolTip = state.IsStop ? "Stop the turn and every agent (Esc)" : "Send (Enter)";
        _action.IsEnabled = state.Enabled;
        _placeholder.Visibility = Input.Text.Length == 0 && !Input.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Shows <paramref name="next"/> as chips, reusing the chip (and decoded thumbnail) of every attachment already shown.</summary>
    private void SetAttachments(List<Attachment> next)
    {
        _attachments = next;
        _chips.Children.Clear();
        foreach (var attachment in next)
        {
            if (!_chipViews.TryGetValue(attachment, out var chip))
            {
                chip = Chip(attachment);
                _chipViews[attachment] = chip;
            }
            _chips.Children.Add(chip);
        }
        foreach (var gone in _chipViews.Keys.Where(a => !next.Contains(a, ByReference.Instance)).ToList())
        {
            _chipViews.Remove(gone);
            _thumbnails.Remove(gone);
        }
        _chips.Visibility = next.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RenderActions();
    }

    private UIElement Chip(Attachment attachment)
    {
        var label = Ui.Text(Attachments.ChipLabel(attachment), small: true);
        label.MaxWidth = 220;
        var remove = Ui.IconButton(Glyphs.Cancel, $"Remove {attachment.Label}", () =>
        {
            SetAttachments([.. _attachments.Where(other => !ReferenceEquals(other, attachment))]);
            Input.Focus();
        });
        remove.Width = 18;
        remove.Height = 18;
        remove.MinHeight = 18;
        remove.FontSize = 9;
        remove.Margin = new Thickness(4, 0, 0, 0);
        var row = Ui.Row(label, remove);
        if (attachment is ImageAttachment image && Thumbnail(image) is BitmapSource source)
        {
            row.Children.Insert(0, new Image { Source = source, Width = 16, Height = 16, Margin = new Thickness(0, 0, 4, 0) });
        }

        var chip = Ui.Card(row, new Thickness(8, 1, 2, 1));
        chip.Margin = new Thickness(0, 0, 4, 4);
        chip.ToolTip = attachment is FileAttachment file ? file.Path
            : attachment is PastedTextAttachment text ? (text.Text.Length > 500 ? text.Text.Substring(0, 500) : text.Text)
            : attachment.Label;

        return chip;
    }

    /// <summary>The image's decoded thumbnail, decoded once per attachment; null when it cannot be decoded (logged).</summary>
    private BitmapSource? Thumbnail(ImageAttachment image)
    {
        if (_thumbnails.TryGetValue(image, out var cached))
        {
            return cached;
        }

        try
        {
            var decoded = Decode(Convert.FromBase64String(image.Data));
            _thumbnails[image] = decoded;

            return decoded;
        }
        catch (Exception error) when (IsUndecodable(error))
        {
            _log($"Showing the thumbnail of {image.Label} failed", error);

            return null;
        }
    }

    private static BitmapSource Decode(byte[] bytes)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = new MemoryStream(bytes);
        bitmap.DecodePixelWidth = 32;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        return bitmap;
    }

    /// <summary>What WPF imaging and base64 decoding throw for data that is not a readable image.</summary>
    private static bool IsUndecodable(Exception error) =>
        error is NotSupportedException || error is FileFormatException || error is FormatException || error is ArgumentException || error is IOException;

    private int ImageCount => _attachments.OfType<ImageAttachment>().Count();

    private long ImageBytes => _attachments.OfType<ImageAttachment>().Sum(i => i.Bytes);

    /// <summary>
    /// A text box only enables Paste for text, so Ctrl+V with an image or copied files on the clipboard would do
    /// nothing; those take the composer's own paste. Text keeps the text box's paste (and <see cref="OnPaste"/>).
    /// </summary>
    private void OnCanPaste(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste)
        {
            return;
        }

        try
        {
            if (!HasNonTextPaste(Clipboard.GetDataObject()))
            {
                return;
            }
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.ExternalException)
        {
            return;
        }
        e.CanExecute = true;
        e.Handled = true;
    }

    private void OnPasteCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste)
        {
            return;
        }

        try
        {
            var data = Clipboard.GetDataObject();
            if (!HasNonTextPaste(data))
            {
                return;
            }

            e.Handled = true;
            PasteNonText(data!);
        }
        catch (Exception error)
        {
            e.Handled = true;
            _log("Pasting failed", error);
            _notice(NoticeLevel.Error, $"Pasting failed: {error.Message}");
        }
    }

    /// <summary>Whether <paramref name="data"/> holds an image or files and no text (text wins, as when copying from Office).</summary>
    private static bool HasNonTextPaste(IDataObject? data) =>
        data is not null && !data.GetDataPresent(DataFormats.UnicodeText)
        && (data.GetDataPresent(PngFormat) || data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.FileDrop));

    /// <summary>The clipboard format browsers and screenshot tools put a lossless, alpha-preserving copy of an image in.</summary>
    private const string PngFormat = "PNG";

    /// <summary>Attaches copied files (images as images, the rest as mentions) or the clipboard image, PNG data first.</summary>
    private void PasteNonText(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            _ = AddDroppedAsync(paths);

            return;
        }
        if (data.GetData(PngFormat) is MemoryStream png)
        {
            AddImages(new[] { ($"Image {++_imageSequence}", (byte[]?)png.ToArray(), "image/png", "") });

            return;
        }
        if (data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
        {
            AddImage(bitmap);
        }
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        try
        {
            if (HasNonTextPaste(e.DataObject))
            {
                e.CancelCommand();
                PasteNonText(e.DataObject);

                return;
            }
            if (!(e.DataObject.GetData(DataFormats.UnicodeText) is string text))
            {
                return;
            }

            if (Attachments.ClassifyPaste(text) == PasteKind.Inline)
            {
                return;
            }

            e.CancelCommand();
            var normalized = text.Replace("\r\n", "\n");
            AddAttachments(new Attachment[] { new PastedTextAttachment($"Pasted text {++_pastedSequence}", normalized) });
        }
        catch (Exception error)
        {
            e.CancelCommand();
            _log("Pasting failed", error);
            _notice(NoticeLevel.Error, $"Pasting failed: {error.Message}");
        }
    }

    private void AddImage(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = new MemoryStream())
        {
            encoder.Save(stream);
            var bytes = stream.ToArray();
            var label = $"Image {++_imageSequence}";
            AddImages(new[] { (label, (byte[]?)bytes, "image/png", "") });
        }
    }

    /// <summary>Attaches images within the count and size limits that decode as images; each rejected one gets a warning.</summary>
    private void AddImages(IReadOnlyList<(string Label, byte[]? Bytes, string MimeType, string Name)> images)
    {
        var admission = Attachments.AdmitImages(ImageCount, ImageBytes, images.Select(i => (i.Name, (long)(i.Bytes?.Length ?? 0))).ToList());
        foreach (var message in admission.Rejected)
        {
            _notice(NoticeLevel.Warning, message);
        }

        var accepted = new List<Attachment>();
        foreach (var image in admission.Accepted.Select(index => images[index]))
        {
            BitmapSource thumbnail;
            try
            {
                thumbnail = Decode(image.Bytes!);
            }
            catch (Exception error) when (IsUndecodable(error))
            {
                _notice(NoticeLevel.Warning, $"Image {image.Label} was not attached: it is not a readable image.");
                continue;
            }
            var attachment = new ImageAttachment(image.Label, Convert.ToBase64String(image.Bytes!), image.MimeType, image.Bytes!.Length);
            _thumbnails[attachment] = thumbnail;
            accepted.Add(attachment);
        }
        if (accepted.Count > 0)
        {
            AddAttachments(accepted);
        }
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!(e.Data.GetData(DataFormats.FileDrop) is string[] paths) || paths.Length == 0)
        {
            return;
        }

        e.Handled = true;
        _ = AddDroppedAsync(paths);
    }

    /// <summary>Attaches dropped paths: images as image attachments (read off the UI thread), anything else as file mentions.</summary>
    internal async Task AddDroppedAsync(IReadOnlyList<string> paths)
    {
        var files = paths.Where(p => !ImageTypes.ContainsKey(Path.GetExtension(p))).Select(p => (Attachment)new FileAttachment(p)).ToList();
        if (files.Count > 0)
        {
            AddAttachments(files);
        }

        var imagePaths = paths.Where(p => ImageTypes.ContainsKey(Path.GetExtension(p))).ToList();
        if (imagePaths.Count == 0)
        {
            return;
        }

        try
        {
            var loaded = await Task.Run(() => imagePaths.Select(p =>
            {
                var size = new FileInfo(p).Length;
                var bytes = size > Attachments.MaxImageBytes ? null : File.ReadAllBytes(p);

                return (Path.GetFileName(p), bytes, ImageTypes[Path.GetExtension(p)], Path.GetFileName(p), size);
            }).ToList());
            var oversized = loaded.Where(i => i.bytes == null).ToList();
            foreach (var image in oversized)
            {
                _notice(NoticeLevel.Warning, $"Image {image.Item1} was not attached: larger than 10 MB.");
            }

            AddImages(loaded.Where(i => i.bytes != null).Select(i => (i.Item1, i.bytes, i.Item3, i.Item4)).ToList());
        }
        catch (Exception error)
        {
            _log("Reading the dropped image failed", error);
            _notice(NoticeLevel.Error, $"Reading the dropped image failed: {error.Message}");
        }
    }

    /// <summary>Attachments are values the user added once; two equal-looking ones are still two chips.</summary>
    private sealed class ByReference : IEqualityComparer<Attachment>
    {
        public static readonly ByReference Instance = new ByReference();

        public bool Equals(Attachment? x, Attachment? y) => ReferenceEquals(x, y);

        public int GetHashCode(Attachment obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
