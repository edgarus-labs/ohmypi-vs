using Newtonsoft.Json.Linq;
using System;
using System.Globalization;

namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>Typed access to one tool call's arguments; every failure is an <see cref="ArgumentException"/> naming the argument.</summary>
internal sealed class VsToolArguments
{
    private readonly JObject _arguments;
    private readonly WorkspaceScope _scope;

    public VsToolArguments(JObject arguments, WorkspaceScope scope)
    {
        _arguments = arguments;
        _scope = scope;
    }

    public string RequiredString(string name, bool allowEmpty = false)
    {
        var value = Token(name) is null ? throw Missing(name) : ReadString(name);
        if (!allowEmpty && value.Trim().Length == 0)
        {
            throw new ArgumentException($"'{name}' must not be empty");
        }

        return value;
    }

    /// <summary>The string, or null when absent or blank.</summary>
    public string? OptionalString(string name)
    {
        if (Token(name) is null)
        {
            return null;
        }

        var value = ReadString(name);

        return value.Trim().Length == 0 ? null : value;
    }

    public int RequiredInt(string name, int min) =>
        OptionalInt(name, min) ?? throw Missing(name);

    public int? OptionalInt(string name, int min)
    {
        var token = Token(name);
        if (token is null)
        {
            return null;
        }

        var message = $"'{name}' must be an integer >= {min}";
        long value;
        if (token.Type == JTokenType.Integer)
        {
            if (!long.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new ArgumentException(message);
            }
        }
        else if (token.Type == JTokenType.Float)
        {
            var number = (double)token;
            if (number != Math.Floor(number) || Math.Abs(number) > int.MaxValue)
            {
                throw new ArgumentException(message);
            }

            value = (long)number;
        }
        else
        {
            throw new ArgumentException(message);
        }
        if (value < min || value > int.MaxValue)
        {
            throw new ArgumentException(message);
        }

        return (int)value;
    }

    public bool? OptionalBool(string name)
    {
        var token = Token(name);
        if (token is null)
        {
            return null;
        }

        if (token.Type != JTokenType.Boolean)
        {
            throw new ArgumentException($"'{name}' must be true or false");
        }

        return (bool)token;
    }

    public string RequiredChoice(string name, string[] allowed)
    {
        if (Token(name) is null)
        {
            throw new ArgumentException($"'{name}' is required (one of: {string.Join(", ", allowed)})");
        }

        return Choice(name, allowed);
    }

    public string OptionalChoice(string name, string[] allowed, string fallback) =>
        Token(name) is null ? fallback : Choice(name, allowed);

    public string RequiredPath(string name) => Inside(name, RequiredString(name));

    public string? OptionalPath(string name)
    {
        var raw = OptionalString(name);

        return raw is null ? null : Inside(name, raw);
    }

    private string Choice(string name, string[] allowed)
    {
        var value = ReadString(name);
        foreach (var option in allowed)
        {
            if (string.Equals(option, value, StringComparison.Ordinal))
            {
                return option;
            }
        }

        throw new ArgumentException($"'{name}' must be one of: {string.Join(", ", allowed)} (got '{value}')");
    }

    private string Inside(string name, string raw)
    {
        string full;
        try
        {
            full = _scope.Resolve(raw);
        }
        catch (InvalidOperationException)
        {
            throw new ArgumentException($"'{name}' is not a local file path: {raw}");
        }
        if (!_scope.Contains(full))
        {
            throw new ArgumentException($"'{name}' must be inside the workspace ({_scope.Cwd}): {full}");
        }

        return full;
    }

    private string ReadString(string name)
    {
        var token = Token(name);
        if (token is null || token.Type != JTokenType.String)
        {
            throw new ArgumentException($"'{name}' must be a string");
        }

        return (string)token!;
    }

    private JToken? Token(string name)
    {
        if (!_arguments.TryGetValue(name, out var token) || token.Type == JTokenType.Null)
        {
            return null;
        }

        return token;
    }

    private static ArgumentException Missing(string name) => new ArgumentException($"'{name}' is required");
}
