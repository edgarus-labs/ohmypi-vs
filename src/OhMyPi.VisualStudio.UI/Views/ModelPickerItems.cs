using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Views
{
    /// <summary>A row of the model list as the row template binds it: a provider header or a model.</summary>
    internal sealed class ModelRowItem : INotifyPropertyChanged
    {
        private bool _isFavorite;

        public ModelRowItem(ModelPickerRow row)
        {
            Entry = row.Entry;
            Provider = row.Provider;
            IsExpanded = row.IsExpanded;
            CountText = row.IsGroup ? row.Count.ToString(CultureInfo.CurrentCulture) : "";
            IsActive = row.IsActive;
            _isFavorite = row.IsFavorite;
            if (Entry != null)
            {
                var details = ModelDetails.From(Entry.Model);
                Context = details.ContextCompact ?? "";
                Price = details.InputCost == ModelDetails.NotProvided && details.OutputCost == ModelDetails.NotProvided
                    ? ""
                    : $"{Cost(details.InputCost)} / {Cost(details.OutputCost)}";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ModelEntry? Entry { get; }
        public bool IsGroup => Entry == null;
        public string Provider { get; }
        /// <summary>The provider name in capitals with hair spaces between the letters, standing in for letter spacing.</summary>
        public string ProviderCaps => string.Join("\u200A", Provider.ToUpperInvariant().Select(c => c.ToString()));
        public bool IsExpanded { get; }
        public string CountText { get; }
        public string Name => Entry?.Name ?? "";
        public string Id => Entry?.Id ?? "";
        /// <summary>The id is shown under the name only when another model of the provider has the same name.</summary>
        public bool ShowId => Entry?.Variant != null;
        /// <summary>The context window as "1M" or "200k"; empty when the catalog does not give it.</summary>
        public string Context { get; } = "";
        public bool HasContext => Context.Length > 0;
        /// <summary>Input and output price per million tokens as "$3 / $15"; empty when the catalog gives neither.</summary>
        public string Price { get; } = "";
        /// <summary>The model the conversation uses now.</summary>
        public bool IsActive { get; }

        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                if (_isFavorite == value) return;
                _isFavorite = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
                FavoriteChanged?.Invoke(this);
            }
        }

        /// <summary>Raised when the favorite flag was changed by the user.</summary>
        public event Action<ModelRowItem>? FavoriteChanged;

        public string AutomationName => IsGroup
            ? $"{Provider}, {CountText} models, {(IsExpanded ? "expanded" : "collapsed")}"
            : $"{Name}, {Provider}, {Id}{(IsActive ? ", current model" : "")}{(IsFavorite ? ", favorite" : "")}";

        private static string Cost(string cost) => cost == ModelDetails.NotProvided ? "n/a" : cost;
    }
}
