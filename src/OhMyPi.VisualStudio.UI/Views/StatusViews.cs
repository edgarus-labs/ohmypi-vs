using Omp.Core;
using System;
using System.Windows;
using System.Windows.Controls;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>Error banner while OMP cannot be used (not found, not available, failed), with settings, log and restart actions.</summary>
internal sealed class Banner : Border
{
    private readonly TextBlock _text;

    /// <summary>
    /// Initializes a new instance of the Banner class with the specified actions for opening settings, showing logs, and restarting the application.
    /// </summary>
    /// <param name="openSettings">The open settings.</param>
    /// <param name="showLog">The show log.</param>
    /// <param name="restart">The restart.</param>
    public Banner(Action openSettings, Action showLog, Action restart)
    {
        Margin = new Thickness(8, 6, 8, 0);
        Padding = new Thickness(10, 6, 10, 6);
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        this.Theme(BorderBrushProperty, ThemeKeys.ErrorBorder);
        Visibility = Visibility.Collapsed;
        _text = Ui.Text("", ThemeKeys.Error, wrap: true);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_text, System.Windows.Automation.AutomationLiveSetting.Assertive);
        var icon = Ui.Icon(Glyphs.Error, ThemeKeys.Error, 13);
        icon.Margin = new Thickness(0, 2, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (label, action) in new (string, Action)[] { ("Open Settings", openSettings), ("Show Log", showLog), ("Restart", restart) })
        {
            var button = Ui.Button(label, action);
            button.Margin = new Thickness(0, 0, 6, 0);
            actions.Children.Add(button);
        }
        var body = new StackPanel();
        body.Children.Add(_text);
        body.Children.Add(actions);
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(body);
        Child = row;
    }

    /// <summary>
    /// Updates the UI visibility, tooltip, and text content to reflect the current OMP availability and connection status, announcing the result to the user if a message is present.
    /// </summary>
    /// <param name="unavailable">The unavailable.</param>
    /// <param name="connection">The connection.</param>
    public void Render(OmpUnavailable? unavailable, ConnectionStatus connection)
    {
        string? message = null;
        if (unavailable is not null)
        {
            message = $"{(unavailable.ExecutableNotFound ? "OMP not found" : "OMP is not available")}: {unavailable.Message}";
        }
        else if (connection.State == ConnectionState.Failed)
        {
            message = $"OMP stopped{(string.IsNullOrEmpty(connection.Detail) ? "" : $": {connection.Detail}")}";
        }

        Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        ToolTip = message;
        if (_text.Text == (message ?? ""))
        {
            return;
        }

        _text.Text = message ?? "";
        if (message is not null)
        {
            Ui.Announce(_text);
        }
    }
}
