using Omp.Core;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>Agent detail: status/model/activity/tools/tokens/cost, its transcript, and Steer / Cancel.</summary>
internal sealed class AgentDetail : Border
{
    private readonly IAgentActions _actions;
    private readonly TextBlock _summary;
    private readonly Button _cancel;
    private readonly StackPanel _steerBox;
    private readonly TextBox _steerInput;
    private readonly StackPanel _confirm;
    private readonly ContentControl _transcript = new ContentControl { Focusable = false };
    private AgentView _agent;

    /// <summary>
    /// Initializes a new instance of the AgentDetail class with the specified agent view and action handlers, configuring the user interface components for agent steering, transcript loading, and cancellation.
    /// </summary>
    /// <param name="agent">The agent.</param>
    /// <param name="actions">The actions.</param>
    public AgentDetail(AgentView agent, IAgentActions actions)
    {
        _agent = agent;
        _actions = actions;
        Margin = new Thickness(22, 2, 4, 6);
        Padding = new Thickness(10, 6, 10, 8);
        this.Styled("Omp.Card");
        _summary = Ui.Text("", ThemeKeys.Muted, wrap: true, small: true);

        var transcriptButton = Ui.Button("Transcript", () => _ = LoadTranscriptAsync());
        var steerButton = Ui.Button("Steer", ShowSteer);
        _cancel = Ui.Button("Cancel", ShowConfirm);
        var actionsRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var button in new[] { transcriptButton, steerButton, _cancel })
        {
            button.Margin = new Thickness(0, 0, 6, 0);
            actionsRow.Children.Add(button);
        }

        _steerInput = new TextBox { AcceptsReturn = false, MinWidth = 160 }.Styled("Omp.TextBox");
        Ui.AutomationName(_steerInput, "Message for the agent");
        _steerInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = SteerAsync();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _steerBox!.Visibility = Visibility.Collapsed;
            }
        };
        var send = Ui.Button("Send", () => _ = SteerAsync(), "Omp.PrimaryButton");
        send.Margin = new Thickness(6, 0, 0, 0);
        var steerRow = new DockPanel();
        DockPanel.SetDock(send, Dock.Right);
        steerRow.Children.Add(send);
        steerRow.Children.Add(_steerInput);
        _steerBox = Ui.Column(2, Ui.Muted("Message for the running agent"), steerRow);
        _steerBox.Margin = new Thickness(0, 6, 0, 0);
        _steerBox.Visibility = Visibility.Collapsed;

        var confirmText = Ui.Text("", wrap: true, small: true);
        var yes = Ui.Button("Cancel Agent", () => _ = CancelAsync(), "Omp.PrimaryButton");
        var no = Ui.Button("Keep Running", () => _confirm!.Visibility = Visibility.Collapsed);
        yes.Margin = new Thickness(0, 0, 6, 0);
        _confirm = Ui.Column(4, confirmText, Ui.Row(yes, no));
        _confirm.Margin = new Thickness(0, 6, 0, 0);
        _confirm.Visibility = Visibility.Collapsed;
        _confirm.Tag = confirmText;

        Child = Ui.Column(0, _summary, actionsRow, _steerBox, _confirm, _transcript);
    }

    /// <summary>
    /// Updates the agent view state and synchronizes the associated UI elements based on the agent&apos;s current status.
    /// </summary>
    /// <param name="agent">The agent.</param>
    public void Update(AgentView agent)
    {
        _agent = agent;
        _summary.Text = Format.AgentSummary(agent, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var active = agent.Status == AgentStatus.Running || agent.Status == AgentStatus.Pending;
        _cancel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (!active)
        {
            _confirm.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Displays the steering input interface and sets the focus to the steering input field.
    /// </summary>
    private void ShowSteer()
    {
        _steerBox.Visibility = Visibility.Visible;
        _steerInput.Focus();
    }

    /// <summary>
    /// Displays a confirmation dialog containing the agent&apos;s name, identifier, and activity details to verify the cancellation request.
    /// </summary>
    private void ShowConfirm()
    {
        ((TextBlock)_confirm.Tag).Text = $"Cancel agent {_agent.Name} ({_agent.Id})?{(string.IsNullOrEmpty(_agent.Activity) ? "" : "\n" + _agent.Activity)}";
        _confirm.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Asynchronously sends the steering input text to the agent and resets the input interface upon successful completion.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private Task SteerAsync()
    {
        var message = _steerInput.Text.Trim();
        if (message.Length == 0)
        {
            return Task.CompletedTask;
        }

        var id = _agent.Id;

        return _actions.RunAsync("Steer agent", async () =>
        {
            await _actions.SteerAsync(id, message);
            _steerInput.Text = "";
            _steerBox.Visibility = Visibility.Collapsed;
        });
    }

    /// <summary>
    /// Asynchronously requests the cancellation of the current agent and updates the user interface based on the operation&apos;s result.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private Task CancelAsync()
    {
        var agent = _agent;
        _confirm.Visibility = Visibility.Collapsed;

        return _actions.RunAsync("Cancel agent", async () =>
        {
            var cancelled = await _actions.CancelAsync(agent.Id);
            if (!cancelled)
            {
                _actions.Notice(NoticeLevel.Info, $"Agent {agent.Name} was not running.");
            }
        });
    }

    /// <summary>
    /// Asynchronously retrieves the agent&apos;s transcript and renders it as a scrollable UI component with an option to copy the content as Markdown.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private Task LoadTranscriptAsync()
    {
        var agent = _agent;
        _transcript.Content = Ui.Muted("Loading transcript…");

        return _actions.RunAsync("Open agent transcript", async () =>
        {
            try
            {
                var items = await _actions.TranscriptAsync(agent.Id);
                var list = new StackPanel();
                TranscriptItem? previous = null;
                foreach (var item in items)
                {
                    var element = ItemRenderer.Render(item, _actions.RenderContext);
                    element.Margin = new Thickness(0, ItemRenderer.GapAbove(item, previous), 0, 0);
                    list.Children.Add(element);
                    previous = item;
                }
                if (items.Count == 0)
                {
                    list.Children.Add(Ui.Muted("The transcript is empty."));
                }

                var copy = Ui.Link("Copy as Markdown", () => _actions.RenderContext.Copy(Format.TranscriptToMarkdown($"{agent.Name} ({agent.Id})", items)));
                copy.HorizontalAlignment = HorizontalAlignment.Left;
                copy.Margin = new Thickness(0, 0, 0, 4);
                var scroller = new ScrollViewer { Content = list, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                var panel = Ui.Column(0, copy, scroller);
                panel.Margin = new Thickness(0, 8, 0, 0);
                _transcript.Content = panel;
            }
            catch
            {
                _transcript.Content = null;

                throw;
            }
        });
    }
}
