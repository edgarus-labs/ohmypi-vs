using Omp.Core;
using System.Globalization;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>What the picker's details area shows about a model; every field is text, "Not provided" when the catalog has nothing for it.</summary>
internal sealed class ModelDetails
{
    /// <summary>
    /// The not provided.
    /// </summary>
    public const string NotProvided = "Not provided";

    private ModelDetails(ModelView model)
    {
        Name = model.Name;
        Provider = model.Provider;
        Id = model.Id;
        Context = Tokens(model.ContextWindow);
        MaxOutput = Tokens(model.MaxTokens);
        InputTypes = model.Input.Count > 0 ? string.Join(", ", model.Input) : NotProvided;
        Reasoning = model.ThinkingEfforts.Count > 0 ? string.Join(", ", model.ThinkingEfforts) : model.Reasoning ? "Supported" : NotProvided;
        InputPrice = Price(model.Cost?.Input);
        OutputPrice = Price(model.Cost?.Output);
        InputCost = Cost(model.Cost?.Input);
        OutputCost = Cost(model.Cost?.Output);
        ContextCompact = model.ContextWindow > 0 ? Format.CompactTokens(model.ContextWindow.Value) : null;
        OutputCompact = model.MaxTokens > 0 ? Format.CompactTokens(model.MaxTokens.Value) : null;
    }

    /// <summary>
    /// Creates a new instance of ModelDetails from the specified ModelView.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <returns>The model details result.</returns>
    public static ModelDetails From(ModelView model) => new ModelDetails(model);

    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the provider.
    /// </summary>
    public string Provider { get; }

    /// <summary>
    /// Gets the id.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the context.
    /// </summary>
    public string Context { get; }

    /// <summary>
    /// Gets the max output.
    /// </summary>
    public string MaxOutput { get; }

    /// <summary>
    /// Gets the input types.
    /// </summary>
    public string InputTypes { get; }

    /// <summary>
    /// Gets the reasoning.
    /// </summary>
    public string Reasoning { get; }

    /// <summary>USD per million tokens.</summary>
    public string InputPrice { get; }

    /// <summary>
    /// Gets the output price.
    /// </summary>
    public string OutputPrice { get; }

    /// <summary>USD per million input tokens without the unit, as "$3"; "Not provided" when unknown.</summary>
    public string InputCost { get; }

    /// <summary>
    /// Gets the output cost.
    /// </summary>
    public string OutputCost { get; }

    /// <summary>The context window as "200k"; null when the catalog does not give it.</summary>
    public string? ContextCompact { get; }

    /// <summary>The output limit as "64k"; null when the catalog does not give it.</summary>
    public string? OutputCompact { get; }

    /// <summary>
    /// Formats the specified token count into a compact string representation or returns a default value if the count is not provided or is non-positive.
    /// </summary>
    /// <param name="count">The count.</param>
    /// <returns>The string result.</returns>
    private static string Tokens(long? count) => count > 0 ? Format.CompactTokens(count.Value) + " tokens" : NotProvided;

    /// <summary>
    /// Formats the price per million tokens as a string based on the provided USD value.
    /// </summary>
    /// <param name="usdPerMillion">The usd per million.</param>
    /// <returns>The string result.</returns>
    private static string Price(double? usdPerMillion) => usdPerMillion.HasValue ? Cost(usdPerMillion) + " / 1M tokens" : NotProvided;

    /// <summary>
    /// Formats the provided cost per million USD as a currency string or returns a default value if no cost is specified.
    /// </summary>
    /// <param name="usdPerMillion">The usd per million.</param>
    /// <returns>The string result.</returns>
    private static string Cost(double? usdPerMillion) =>
        usdPerMillion.HasValue ? "$" + usdPerMillion.Value.ToString("0.######", CultureInfo.InvariantCulture) : NotProvided;
}
