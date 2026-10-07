namespace OhMyPi.VisualStudio.UI
{
    /// <summary>
    /// Things the user closed in the chat (such as a finished todo list), remembered across Visual Studio sessions.
    /// A host offers it by implementing this interface next to <see cref="IOmpHost"/>; without it, closing lasts
    /// until the chat is recreated.
    /// </summary>
    public interface IDismissals
    {
        bool IsDismissed(string key);

        void Dismiss(string key);
    }
}
