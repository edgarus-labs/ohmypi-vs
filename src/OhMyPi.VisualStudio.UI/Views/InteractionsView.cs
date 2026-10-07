using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Omp.Core;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Views
{
    /// <summary>
    /// Pending OMP questions (confirm, select, input, editor, ask) as cards above the composer, with timeout
    /// countdowns. Nothing is ever answered automatically; secret input is masked and never echoed. A new card is
    /// announced to screen readers; an answer that cannot be delivered keeps its card.
    /// </summary>
    internal sealed class InteractionsView : StackPanel
    {
        private readonly Action<string, InteractionResponse> _respond;
        private readonly Action<string> _openUrl;
        private readonly Action<Exception> _failed;
        private readonly Action _focusElsewhere;
        private readonly Dictionary<string, Border> _cards = new Dictionary<string, Border>(StringComparer.Ordinal);
        private readonly Dictionary<string, (DateTime ExpiresAt, TextBlock Label)> _expiries = new Dictionary<string, (DateTime, TextBlock)>(StringComparer.Ordinal);
        private readonly DispatcherTimer _timer;
        private int _urlSequence;

        /// <param name="respond">Delivers an answer to OMP; throws when it cannot.</param>
        /// <param name="openUrl">Opens a URL the user confirmed.</param>
        /// <param name="failed">Reports an answer that could not be delivered.</param>
        /// <param name="focusElsewhere">Takes keyboard focus when the last card is answered from the keyboard.</param>
        public InteractionsView(Action<string, InteractionResponse> respond, Action<string> openUrl, Action<Exception> failed, Action focusElsewhere)
        {
            _respond = respond;
            _openUrl = openUrl;
            _failed = failed;
            _focusElsewhere = focusElsewhere;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, __) => Tick();
            Visibility = Visibility.Collapsed;
        }

        /// <summary>Raised when cards are added or removed.</summary>
        public event Action? Changed;

        /// <summary>Pending OMP requests (URL prompts excluded).</summary>
        public int PendingCount => _cards.Keys.Count(id => !id.StartsWith("url-", StringComparison.Ordinal));

        /// <summary>Shows a card for <paramref name="request"/>; a request already shown keeps its card and what the user typed.</summary>
        public void Add(InteractionRequest request)
        {
            if (_cards.ContainsKey(request.Id)) return;
            var countdown = Ui.Muted("");
            var title = Ui.Text(Title(request), weight: FontWeights.SemiBold, wrap: true);
            var header = new DockPanel();
            DockPanel.SetDock(countdown, Dock.Right);
            header.Children.Add(countdown);
            header.Children.Add(title);
            var body = new List<UIElement?> { header };
            body.AddRange(Body(request));
            Show(request.Id, title, body);
            if (request.TimeoutMs.HasValue && request.TimeoutMs.Value > 0)
            {
                _expiries[request.Id] = (DateTime.UtcNow.AddMilliseconds(request.TimeoutMs.Value), countdown);
                Tick();
                if (!_timer.IsEnabled) _timer.Start();
            }
        }

        /// <summary>OMP asked to open a web page; only opened after the user confirms.</summary>
        public void AddOpenUrl(string url, string? instructions)
        {
            var id = $"url-{++_urlSequence}";
            var text = Ui.Text("OMP wants to open a URL in your browser.", weight: FontWeights.SemiBold, wrap: true);
            var detail = Ui.Text(string.IsNullOrEmpty(instructions) ? url : $"{instructions}\n{url}", ThemeKeys.Muted, wrap: true, small: true);
            var open = Ui.Button("Open", () =>
            {
                Remove(id);
                _openUrl(url);
            }, "Omp.PrimaryButton");
            var dismiss = Ui.Button("Dismiss", () => Remove(id));
            Show(id, text, new List<UIElement?> { text, detail, Actions(open, dismiss) });
        }

        /// <summary>
        /// Removes a card. When it held keyboard focus, focus moves to the next card itself (never into its buttons,
        /// so a repeated Enter cannot answer a request the user has not read), or elsewhere when none is left.
        /// </summary>
        public void Remove(string id)
        {
            if (!_cards.TryGetValue(id, out var card)) return;
            var hadFocus = card.IsKeyboardFocusWithin;
            var index = Children.IndexOf(card);
            Children.Remove(card);
            _cards.Remove(id);
            _expiries.Remove(id);
            if (_expiries.Count == 0) _timer.Stop();
            Visibility = _cards.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (hadFocus)
            {
                if (Children.Count > 0) ((UIElement)Children[Math.Min(index, Children.Count - 1)]).Focus();
                else _focusElsewhere();
            }
            Changed?.Invoke();
        }

        public void StopTimer() => _timer.Stop();

        private void Show(string id, TextBlock title, List<UIElement?> body)
        {
            var card = new Border
            {
                Child = Ui.Column(8, body.ToArray()),
                Padding = new Thickness(12, 8, 12, 10),
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Focusable = true,
            };
            card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PopupBackground);
            card.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.InputBorderFocused);
            card.SetResourceReference(FocusVisualStyleProperty, "Omp.FocusVisual");
            Ui.AutomationName(card, title.Text);
            KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Continue);
            System.Windows.Automation.AutomationProperties.SetLiveSetting(title, System.Windows.Automation.AutomationLiveSetting.Assertive);
            _cards[id] = card;
            Children.Add(card);
            Visibility = Visibility.Visible;
            Ui.Announce(title);
            Changed?.Invoke();
        }

        private static string Title(InteractionRequest request)
        {
            switch (request)
            {
                case ConfirmRequest confirm: return confirm.Title;
                case SelectRequest select: return select.Title;
                case InputRequest input: return input.Title;
                case EditorRequest editor: return editor.Title;
                case AskRequest ask: return ask.Questions.Count == 1 ? "Question" : $"{ask.Questions.Count} questions";
                default: return "OMP request";
            }
        }

        /// <summary>Delivers the answer and removes the card; when OMP cannot take it, the card stays and the failure is reported.</summary>
        private bool Respond(string id, InteractionResponse response)
        {
            try
            {
                _respond(id, response);
            }
            catch (Exception error)
            {
                _failed(error);
                return false;
            }
            Remove(id);
            return true;
        }

        private Button CancelButton(string id) => Ui.Button("Cancel", () => Respond(id, InteractionResponse.Cancelled()));

        private static UIElement Actions(params Button[] buttons)
        {
            var row = new WrapPanel();
            foreach (var button in buttons)
            {
                button.Margin = new Thickness(0, 0, 6, 0);
                row.Children.Add(button);
            }
            return row;
        }

        private IEnumerable<UIElement?> Body(InteractionRequest request)
        {
            switch (request)
            {
                case ConfirmRequest confirm:
                    return new[]
                    {
                        string.IsNullOrEmpty(confirm.Message) ? null : Ui.Text(confirm.Message!, wrap: true),
                        Actions(
                            Ui.Button("Approve", () => Respond(confirm.Id, InteractionResponse.FromConfirmed(true)), "Omp.PrimaryButton"),
                            Ui.Button("Deny", () => Respond(confirm.Id, InteractionResponse.FromConfirmed(false)))),
                    };
                case SelectRequest select:
                {
                    var options = new StackPanel();
                    foreach (var option in select.Options)
                    {
                        var content = Ui.Column(0, Ui.Text(option.Label, wrap: true), string.IsNullOrEmpty(option.Description) ? null : Ui.Muted(option.Description!, wrap: true));
                        var button = new Button { Content = content, ToolTip = option.Description ?? option.Label }.Styled("Omp.RowButton");
                        button.SetResourceReference(Control.BorderBrushProperty, ThemeKeys.CardBorder);
                        button.Margin = new Thickness(0, 0, 0, 4);
                        Ui.AutomationName(button, option.Label);
                        var label = option.Label;
                        button.Click += (_, __) => Respond(select.Id, InteractionResponse.FromValue(label));
                        options.Children.Add(button);
                    }
                    return new UIElement?[] { options, Actions(CancelButton(select.Id)) };
                }
                case InputRequest input:
                    return InputBody(input);
                case EditorRequest editor:
                {
                    var area = new TextBox
                    {
                        Text = editor.Prefill ?? "",
                        AcceptsReturn = true,
                        AcceptsTab = true,
                        TextWrapping = TextWrapping.Wrap,
                        MinHeight = 96,
                        MaxHeight = 280,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    }.Styled("Omp.TextBox");
                    area.SetResourceReference(Control.FontFamilyProperty, "Omp.MonoFont");
                    Ui.AutomationName(area, editor.Title);
                    return new UIElement?[]
                    {
                        area,
                        Actions(Ui.Button("Submit", () => Respond(editor.Id, InteractionResponse.FromValue(area.Text)), "Omp.PrimaryButton"), CancelButton(editor.Id)),
                    };
                }
                case AskRequest ask:
                    return AskBody(ask);
                default:
                    return new UIElement?[] { Actions(CancelButton(request.Id)) };
            }
        }

        private IEnumerable<UIElement?> InputBody(InputRequest input)
        {
            Func<string> read;
            Action clear;
            Control field;
            if (input.Secret)
            {
                var password = new PasswordBox().Styled("Omp.PasswordBox");
                read = () => password.Password;
                clear = () => password.Clear();
                field = password;
            }
            else
            {
                var text = new TextBox().Styled("Omp.TextBox");
                read = () => text.Text;
                clear = () => text.Clear();
                field = text;
            }
            Ui.AutomationName(field, input.Title);
            if (!string.IsNullOrEmpty(input.Placeholder)) field.ToolTip = input.Placeholder;
            void Submit()
            {
                if (Respond(input.Id, InteractionResponse.FromValue(read()))) clear();
            }
            field.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                Submit();
            };
            var placeholder = string.IsNullOrEmpty(input.Placeholder) ? null : Ui.Muted(input.Placeholder!, wrap: true);
            return new UIElement?[] { placeholder, field, Actions(Ui.Button("Submit", Submit, "Omp.PrimaryButton"), CancelButton(input.Id)) };
        }

        private IEnumerable<UIElement?> AskBody(AskRequest ask)
        {
            var readers = new List<Func<AskQuestionState>>();
            var elements = new List<UIElement?>();
            for (var q = 0; q < ask.Questions.Count; q++)
            {
                var question = ask.Questions[q];
                var group = $"ask-{ask.Id}-{q}";
                var controls = new List<(AskOptionView Option, System.Windows.Controls.Primitives.ToggleButton Control)>();
                var panel = new StackPanel();
                var legend = Ui.Text(string.IsNullOrEmpty(question.Header) ? question.Question : $"{question.Header}: {question.Question}", weight: FontWeights.SemiBold, wrap: true);
                legend.Margin = new Thickness(0, 0, 0, 4);
                panel.Children.Add(legend);
                for (var o = 0; o < question.Options.Count; o++)
                {
                    var option = question.Options[o];
                    var label = new WrapPanel();
                    label.Children.Add(Ui.Text(option.Label, wrap: true));
                    if (question.Recommended == o)
                    {
                        var badge = Ui.Card(Ui.Text("recommended", ThemeKeys.Link, small: true), new Thickness(6, 0, 6, 0));
                        badge.Margin = new Thickness(6, 0, 0, 0);
                        label.Children.Add(badge);
                    }
                    var content = Ui.Column(0, label, string.IsNullOrEmpty(option.Description) ? null : Ui.Muted(option.Description!, wrap: true));
                    System.Windows.Controls.Primitives.ToggleButton control = question.Multi
                        ? new CheckBox { Content = content }.Styled("Omp.MultiCheckBox")
                        : (System.Windows.Controls.Primitives.ToggleButton)new RadioButton { Content = content, GroupName = group }.Styled("Omp.RadioButton");
                    control.Margin = new Thickness(0, 2, 0, 2);
                    if (!string.IsNullOrEmpty(option.Preview)) control.ToolTip = option.Preview;
                    Ui.AutomationName(control, option.Label);
                    controls.Add((option, control));
                    panel.Children.Add(control);
                }
                var other = new TextBox { Margin = new Thickness(0, 4, 0, 0), ToolTip = "Other…" }.Styled("Omp.TextBox");
                Ui.AutomationName(other, $"{question.Question} — other");
                if (!question.Multi)
                {
                    other.TextChanged += (_, __) =>
                    {
                        if (other.Text.Trim().Length == 0) return;
                        foreach (var (_, control) in controls) control.IsChecked = false;
                    };
                    foreach (var (_, control) in controls) control.Checked += (_, __) => other.Text = "";
                }
                panel.Children.Add(Ui.Muted("Other…"));
                panel.Children.Add(other);
                readers.Add(() => new AskQuestionState(controls.Where(c => c.Control.IsChecked == true).Select(c => c.Option.Label).ToList(), other.Text));
                elements.Add(Ui.Card(panel, new Thickness(10, 6, 10, 8)));
            }
            elements.Add(Actions(
                Ui.Button("Submit", () => Respond(ask.Id, InteractionResponse.FromAnswers(AskAnswers.Build(ask.Questions, readers.Select(read => read()).ToList()))), "Omp.PrimaryButton"),
                CancelButton(ask.Id)));
            return elements;
        }

        private void Tick()
        {
            var now = DateTime.UtcNow;
            foreach (var pair in _expiries.Values)
            {
                var seconds = Math.Max(0, (int)Math.Ceiling((pair.ExpiresAt - now).TotalSeconds));
                pair.Label.Text = $"{seconds}s left";
            }
            if (_expiries.Count == 0) _timer.Stop();
        }
    }
}
