using System;
using System.Text;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>Lines waiting to be written, so many lines from background threads reach the Output pane in one UI-thread call.</summary>
internal sealed class PendingText
{
    private readonly StringBuilder _text = new StringBuilder();
    private bool _flushScheduled;

    /// <summary>Adds a line; true when the caller must schedule a flush because none is pending.</summary>
    public bool Append(string line)
    {
        lock (_text)
        {
            _text.Append(line).Append(Environment.NewLine);
            if (_flushScheduled)
            {
                return false;
            }

            _flushScheduled = true;

            return true;
        }
    }

    /// <summary>Takes everything appended so far; the next line schedules a flush again.</summary>
    public string Drain()
    {
        lock (_text)
        {
            var text = _text.ToString();
            _text.Clear();
            _flushScheduled = false;

            return text;
        }
    }
}
