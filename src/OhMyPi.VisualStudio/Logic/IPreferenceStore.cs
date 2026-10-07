namespace OhMyPi.VisualStudio.Logic;

/// <summary>String values the model preferences are persisted in.</summary>
internal interface IPreferenceStore
{
    string? Read(string key);

    void Write(string key, string value);
}
