using System;

namespace Omp.Core.Internal;

/// <summary>
/// Invokes every subscriber; a throwing subscriber is logged with the event's <c>name</c> and does not stop the others.
/// </summary>
internal static class Listeners
{
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

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
