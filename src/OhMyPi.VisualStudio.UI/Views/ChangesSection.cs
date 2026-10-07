using OhMyPi.VisualStudio.UI.Model;
using Omp.Core.Changes;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>Changes: one row per file changed by OMP (<c>M AuthService.cs  src\auth · +3 −1</c>); click for the native diff.</summary>
internal sealed class ChangesSection : Section
{
    private readonly Action<string> _openDiff;
    private readonly Action<string> _openFile;

    public ChangesSection(Action<string> openDiff, Action<string> openFile) : base("Changes")
    {
        _openDiff = openDiff;
        _openFile = openFile;
    }

    public void Render(IReadOnlyList<TrackedChange> changes, string? cwd)
    {
        SetTitle(ChangeRows.Title(changes.Count));
        Rows.Children.Clear();
        foreach (var change in changes)
        {
            Rows.Children.Add(Row(change, cwd));
        }

        if (changes.Count == 0)
        {
            Rows.Children.Add(AgentsSection.EmptyRow("No files changed by OMP yet."));
        }
    }

    private UIElement Row(TrackedChange change, string? cwd)
    {
        var letter = Ui.Text(ChangeRows.StatusLetter(change.Status),
            change.Status == ChangeStatus.Added ? ThemeKeys.Success : change.Status == ChangeStatus.Deleted ? ThemeKeys.Error : ThemeKeys.Progress,
            weight: FontWeights.SemiBold, small: true);
        letter.Width = 16;
        var name = Ui.Text(ChangeRows.FileName(change), small: true);
        name.Margin = new Thickness(0, 0, 8, 0);
        var detail = Ui.Muted(ChangeRows.Detail(change, cwd));
        var content = new DockPanel();
        DockPanel.SetDock(letter, Dock.Left);
        DockPanel.SetDock(name, Dock.Left);
        content.Children.Add(letter);
        content.Children.Add(name);
        content.Children.Add(detail);
        var counts = change.Status == ChangeStatus.Deleted ? "" : $" +{change.Added} −{change.Removed}";
        var tooltip = $"{change.Path}\n{ChangeRows.StatusText(change.Status)}{counts}\nClick for the diff";
        var row = new Button { Content = content, ToolTip = tooltip }.Styled("Omp.RowButton");
        Ui.AutomationName(row, $"Diff {ChangeRows.FileName(change)} {ChangeRows.StatusText(change.Status)}");
        var path = change.Path;
        row.Click += (_, __) => _openDiff(path);
        var open = Ui.IconButton(Glyphs.OpenFile, "Open file", () => _openFile(path));
        open.Width = 22;
        open.Height = 22;
        open.Visibility = change.Status == ChangeStatus.Deleted ? Visibility.Collapsed : Visibility.Visible;
        var line = new DockPanel();
        DockPanel.SetDock(open, Dock.Right);
        line.Children.Add(open);
        line.Children.Add(row);

        return line;
    }
}
