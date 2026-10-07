using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Omp.Core;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Views
{
    /// <summary>
    /// Popup above the prompt listing the OMP slash commands that match the command word being typed. Keyboard focus
    /// stays in the prompt: Up/Down move the selection, Tab or Enter complete it, Esc closes the list.
    /// </summary>
    internal sealed class SlashCompletion
    {
        private const int MaxRows = 50;

        private readonly TextBox _input;
        private readonly ListBox _list;
        private readonly Popup _popup;
        private IReadOnlyList<SlashCommandView> _commands = Array.Empty<SlashCommandView>();

        public SlashCompletion(TextBox input)
        {
            _input = input;
            _list = new ListBox { MaxHeight = 260, Focusable = false }.Styled("Omp.ListBox");
            Ui.AutomationName(_list, "Slash commands");
            _list.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (ItemsControl.ContainerFromElement(_list, (DependencyObject)e.OriginalSource) is ListBoxItem item)
                {
                    _list.SelectedItem = item;
                    Accept();
                    e.Handled = true;
                }
            };
            _popup = Ui.Popup(input, _list);
            _popup.StaysOpen = true;
            _popup.PlacementRectangle = Rect.Empty;
            input.TextChanged += (_, __) => Refresh();
            input.LostKeyboardFocus += (_, __) => _popup.IsOpen = false;
        }

        public IReadOnlyList<SlashCommandView> Commands
        {
            get => _commands;
            set
            {
                _commands = value ?? Array.Empty<SlashCommandView>();
                if (_popup.IsOpen) Refresh();
            }
        }

        /// <summary>Handles a key pressed in the prompt; true when the completion list consumed it.</summary>
        public bool HandleKey(Key key)
        {
            if (!_popup.IsOpen) return false;
            switch (key)
            {
                case Key.Down:
                    Move(1);
                    return true;
                case Key.Up:
                    Move(-1);
                    return true;
                case Key.Tab:
                case Key.Enter:
                    return Accept();
                case Key.Escape:
                    _popup.IsOpen = false;
                    return true;
                default:
                    return false;
            }
        }

        private void Refresh()
        {
            var query = SlashCommands.Query(_input.Text);
            var matches = query == null ? Array.Empty<SlashCommandView>() : SlashCommands.Match(_commands, query);
            if (matches.Count == 0 || (matches.Count == 1 && string.Equals(matches[0].Name, query, StringComparison.OrdinalIgnoreCase)))
            {
                _popup.IsOpen = false;
                return;
            }
            _list.Items.Clear();
            foreach (var command in matches.Take(MaxRows)) _list.Items.Add(Row(command));
            _list.SelectedIndex = 0;
            _popup.Width = Math.Max(240, _input.ActualWidth) * Ui.ZoomOf(_input);
            _popup.IsOpen = true;
        }

        private static ListBoxItem Row(SlashCommandView command)
        {
            var name = Ui.Text("/" + command.Name);
            name.SetResourceReference(TextBlock.FontFamilyProperty, "Omp.MonoFont");
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(name);
            if (!string.IsNullOrEmpty(command.Hint))
            {
                var hint = Ui.Muted(command.Hint!);
                hint.Margin = new Thickness(6, 0, 0, 0);
                head.Children.Add(hint);
            }
            var content = new StackPanel();
            content.Children.Add(head);
            if (!string.IsNullOrEmpty(command.Description))
            {
                var description = Ui.Muted(command.Description!, small: true);
                description.TextTrimming = TextTrimming.CharacterEllipsis;
                content.Children.Add(description);
            }
            var item = new ListBoxItem { Content = content, Tag = command, Focusable = false };
            Ui.AutomationName(item, "/" + command.Name);
            return item;
        }

        private void Move(int delta)
        {
            if (_list.Items.Count == 0) return;
            var index = (_list.SelectedIndex + delta + _list.Items.Count) % _list.Items.Count;
            _list.SelectedIndex = index;
            _list.ScrollIntoView(_list.Items[index]);
        }

        private bool Accept()
        {
            if (!(_list.SelectedItem is ListBoxItem { Tag: SlashCommandView command })) return false;
            _popup.IsOpen = false;
            _input.Text = "/" + command.Name + " ";
            _input.CaretIndex = _input.Text.Length;
            return true;
        }
    }
}
