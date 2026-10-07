using System;
using System.Globalization;
using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Model
{
    /// <summary>What the picker's details area shows about a model; every field is text, "Not provided" when the catalog has nothing for it.</summary>
    internal sealed class ModelDetails
    {
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

        public static ModelDetails From(ModelView model) => new ModelDetails(model);

        public string Name { get; }
        public string Provider { get; }
        public string Id { get; }
        public string Context { get; }
        public string MaxOutput { get; }
        public string InputTypes { get; }
        public string Reasoning { get; }
        /// <summary>USD per million tokens.</summary>
        public string InputPrice { get; }
        public string OutputPrice { get; }
        /// <summary>USD per million input tokens without the unit, as "$3"; "Not provided" when unknown.</summary>
        public string InputCost { get; }
        public string OutputCost { get; }
        /// <summary>The context window as "200k"; null when the catalog does not give it.</summary>
        public string? ContextCompact { get; }
        /// <summary>The output limit as "64k"; null when the catalog does not give it.</summary>
        public string? OutputCompact { get; }

        private static string Tokens(long? count) => count > 0 ? Format.CompactTokens(count.Value) + " tokens" : NotProvided;

        private static string Price(double? usdPerMillion) => usdPerMillion.HasValue ? Cost(usdPerMillion) + " / 1M tokens" : NotProvided;

        private static string Cost(double? usdPerMillion) =>
            usdPerMillion.HasValue ? "$" + usdPerMillion.Value.ToString("0.######", CultureInfo.InvariantCulture) : NotProvided;
    }

    internal readonly struct ModelPickerSize
    {
        public ModelPickerSize(double width, double height)
        {
            Width = width;
            Height = height;
        }

        public double Width { get; }
        public double Height { get; }
    }

    /// <summary>Sizes of the model picker popup in unzoomed units.</summary>
    internal static class ModelPickerLayout
    {
        /// <summary>The whole available room, never negative.</summary>
        public static ModelPickerSize Fit(double availableWidth, double availableHeight) =>
            new ModelPickerSize(Math.Max(0, availableWidth), Math.Max(0, availableHeight));
    }
}
