using System;

namespace Omp.Core.Internal;

/// <summary>
/// Invokes every subscriber; a throwing subscriber is logged with the event's <c>name</c> and does not stop the others.
/// </summary>
internal static class Listeners
{
    /// <summary>
    /// Invokes each handler in the provided event invocation list individually, ensuring that exceptions thrown by any single listener are caught and logged without interrupting the execution of remaining handlers.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="name">The name.</param>
    /// <param name="sender">The sender.</param>
    /// <param name="value">The value.</param>
    /// <param name="logger">The logger.</param>
    public static void Raise<T>(EventHandler<T>? handler, string name, object sender, T value, IOmpLogger logger)
    {
        if (handler is null)
        {
            return;
        }

        foreach (EventHandler<T> listener in handler.GetInvocationList())
        {
            try
            {
                listener(sender, value);
            }
            catch (Exception error)
            {
                logger.Error($"OMP {name} listener failed", error);
            }
        }
    }

    /// <summary>
    /// Invokes all registered event handlers for a specified event name, ensuring that exceptions thrown by individual listeners are caught and logged.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    /// <param name="logger">The logger.</param>
    public static void Raise<T>(Action<T>? handler, string name, T value, IOmpLogger logger)
    {
        if (handler is null)
        {
            return;
        }

        foreach (Action<T> listener in handler.GetInvocationList())
        {
            try
            {
                listener(value);
            }
            catch (Exception error)
            {
                logger.Error($"OMP {name} listener failed", error);
            }
        }
    }

    public static void Raise(Action? handler, string name, IOmpLogger logger)
    {
        if (handler is null)
        {
            return;
        }

        foreach (Action listener in handler.GetInvocationList())
        {
            try
            {
                listener();
            }
            catch (Exception error)
            {
                logger.Error($"OMP {name} listener failed", error);
            }
        }
    }

    /// <summary>
    /// Returns the current UTC date and time as a Unix timestamp in milliseconds.
    /// </summary>
    /// <returns>The long result.</returns>
    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
