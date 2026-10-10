using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>Notification text kept to one line, so a long failure (for example every PATH directory tried) does not fill the window.</summary>
internal static class NotificationText
{
    /// <summary>
    /// The max length.
    /// </summary>
    public const int MaxLength = 200;
    /// <summary>
    /// The elided.
    /// </summary>
    private const string Elided = " … (details in the oh-my-pi log)";

    /// <summary>
    /// Truncates the specified message to a maximum length and removes trailing newlines, appending an ellipsis if the text is shortened.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The string result.</returns>
    public static string Summary(string message)
    {
        var text = message.Trim();
        var newline = text.IndexOfAny(['\r', '\n']);
        var cut = newline >= 0;
        if (cut)
        {
            text = text.Substring(0, newline).TrimEnd();
        }

        if (text.Length + (cut ? Elided.Length : 0) > MaxLength)
        {
            text = text.Substring(0, MaxLength - Elided.Length).TrimEnd();
            cut = true;
        }

        return cut ? text + Elided : text;
    }
}

/// <summary>Notifications on screen by message, so a repeated message reuses the open notification instead of stacking another.</summary>
internal sealed class OpenNotifications
{
    private readonly Dictionary<string, Task<string?>> _open = new Dictionary<string, Task<string?>>(StringComparer.Ordinal);

    /// <summary>The choice of the notification showing <paramref name="message"/>; <paramref name="show"/> runs only when none is open.</summary>
    public Task<string?> ShowOnceAsync(string message, Func<Task<string?>> show)
    {
        var choice = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_open)
        {
#pragma warning disable VSTHRD003 // The notification another caller opened; its own UI events complete it.
            if (_open.TryGetValue(message, out var open))
            {
                return open;
            }
#pragma warning restore VSTHRD003
            _open.Add(message, choice.Task);
        }
        _ = ShowCoreAsync(message, show, choice);

        return choice.Task;
    }

    /// <summary>
    /// Asynchronously executes a message display function and completes the provided task completion source with the resulting choice or any encountered exception.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="show">The show.</param>
    /// <param name="choice">The choice.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ShowCoreAsync(string message, Func<Task<string?>> show, TaskCompletionSource<string?> choice)
    {
        string? result = null;
        Exception? failure = null;
        try
        {
            result = await show().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = error;
        }
        lock (_open)
        {
            _open.Remove(message);
        }

        if (failure is null)
        {
            choice.TrySetResult(result);
        }
        else
        {
            choice.TrySetException(failure);
        }
    }
}
