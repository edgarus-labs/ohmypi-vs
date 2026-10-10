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

    /// <summary>
    /// Retrieves and validates an optional integer value by name, ensuring it meets the specified minimum threshold and fits within the 32-bit signed integer range.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="min">The min.</param>
    /// <returns>The int? result.</returns>
    /// <exception cref="ArgumentException">Thrown when an error occurs during execution.</exception>
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

    /// <summary>
    /// Retrieves a boolean value associated with the specified name, returning null if the token is missing or throwing an ArgumentException if the token is not a boolean.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The bool? result.</returns>
    /// <exception cref="ArgumentException">Thrown when an error occurs during execution.</exception>
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

    /// <summary>
    /// Validates that a required token exists for the specified name and returns a value from the provided set of allowed choices, throwing an ArgumentException if the token is missing.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="allowed">The collection of allowed.</param>
    /// <returns>The string result.</returns>
    /// <exception cref="ArgumentException">Thrown when an error occurs during execution.</exception>
    public string RequiredChoice(string name, string[] allowed)
    {
        if (Token(name) is null)
        {
            throw new ArgumentException($"'{name}' is required (one of: {string.Join(", ", allowed)})");
        }

        return Choice(name, allowed);
    }

    /// <summary>
    /// Retrieves a value for the specified name from the allowed options, returning the fallback value if the token is null.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="allowed">The collection of allowed.</param>
    /// <param name="fallback">The fallback.</param>
    /// <returns>The string result.</returns>
    public string OptionalChoice(string name, string[] allowed, string fallback) =>
        Token(name) is null ? fallback : Choice(name, allowed);

    /// <summary>
    /// Returns the fully qualified path for a required resource identified by the specified name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The string result.</returns>
    public string RequiredPath(string name) => Inside(name, RequiredString(name));

    /// <summary>
    /// Retrieves an optional path associated with the specified name, returning a formatted internal path if the value exists or null if it is not found.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The string? result.</returns>
    public string? OptionalPath(string name)
    {
        var raw = OptionalString(name);

        return raw is null ? null : Inside(name, raw);
    }

    /// <summary>
    /// Prompts the user for a string input and validates that it matches one of the specified allowed options, throwing an ArgumentException if the input is invalid.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="allowed">The collection of allowed.</param>
    /// <returns>The string result.</returns>
    /// <exception cref="ArgumentException">Thrown when an error occurs during execution.</exception>
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

    /// <summary>
    /// Resolves a raw path to its full representation and validates that it resides within the current workspace scope.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="raw">The raw.</param>
    /// <returns>The string result.</returns>
    /// <exception cref="ArgumentException">Thrown when an error occurs during execution.</exception>
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

    /// <summary>
    /// Retrieves a string value associated with the specified name, throwing an ArgumentException if the token is missing or is not of a string type.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The string result.</returns>
    /// <exception cref="ArgumentException">Thrown when an error occurs during execution.</exception>
    private string ReadString(string name)
    {
        var token = Token(name);
        if (token is null || token.Type != JTokenType.String)
        {
            throw new ArgumentException($"'{name}' must be a string");
        }

        return (string)token!;
    }

    /// <summary>
    /// Retrieves the associated JSON token for the specified name from the arguments collection, returning null if the token is missing or null.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The jtoken? result.</returns>
    private JToken? Token(string name)
    {
        if (!_arguments.TryGetValue(name, out var token) || token.Type == JTokenType.Null)
        {
            return null;
        }

        return token;
    }

    /// <summary>
    /// Creates an ArgumentException indicating that a required parameter with the specified name is missing.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The argument exception result.</returns>
    private static ArgumentException Missing(string name) => new ArgumentException($"'{name}' is required");
}
