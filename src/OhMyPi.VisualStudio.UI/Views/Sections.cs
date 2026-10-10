using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>Collapsible section with a title row (collapsed by default) and a height-capped body.</summary>
internal abstract class Section : StackPanel
{
    private readonly ToggleButton _header;
    private readonly TextBlock _chevron;
    private readonly TextBlock _title;

    /// <summary>
    /// Initializes a new instance of the Section class with the specified title and configures the associated header and body UI components.
    /// </summary>
    /// <param name="title">The title.</param>
    protected Section(string title)
    {
        _chevron = Ui.Icon(Glyphs.ChevronRight, ThemeKeys.Muted, 9);
        _chevron.Margin = new Thickness(0, 0, 6, 0);
        _title = Ui.Text(title, weight: FontWeights.SemiBold, small: true);
        _header = new ToggleButton { Content = Ui.Row(_chevron, _title) }.Styled("Omp.ExpanderHeader");
        _header.Margin = new Thickness(4, 0, 4, 0);
        Rows = new StackPanel();
        Body = new ScrollViewer
        {
            Content = Rows,
            MaxHeight = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(4, 0, 4, 4),
        };
        _header.Checked += (_, __) => Apply(true);
        _header.Unchecked += (_, __) => Apply(false);
        Children.Add(_header);
        Children.Add(Body);
    }

    /// <summary>
    /// Gets the rows.
    /// </summary>
    protected StackPanel Rows { get; }

    /// <summary>
    /// Gets the body.
    /// </summary>
    protected ScrollViewer Body { get; }

    /// <summary>
    /// Expands the element by checking the header, bringing it into view, and setting the input focus.
    /// </summary>
    public void Expand()
    {
        _header.IsChecked = true;
        _header.BringIntoView();
        _header.Focus();
    }

    /// <summary>
    /// Updates the header text and assigns the specified title to the UI automation name for accessibility.
    /// </summary>
    /// <param name="title">The title.</param>
    protected void SetTitle(string title)
    {
        _title.Text = title;
        Ui.AutomationName(_header, title);
    }

    /// <summary>
    /// Updates the chevron icon and body visibility to reflect the specified open or closed state.
    /// </summary>
    /// <param name="open">The open.</param>
    private void Apply(bool open)
    {
        _chevron.Text = open ? Glyphs.ChevronDown : Glyphs.ChevronRight;
        Body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }
}
