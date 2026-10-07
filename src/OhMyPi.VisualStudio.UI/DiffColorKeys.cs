namespace OhMyPi.VisualStudio.UI
{
    /// <summary>
    /// Resource keys of the diff backgrounds the chat draws changed lines and words with. The host puts the colors of
    /// Visual Studio's own diff window under these keys in a resource dictionary above the chat control.
    /// </summary>
    public static class DiffColorKeys
    {
        public const string AddedLine = "Omp.Diff.AddedLine";
        public const string RemovedLine = "Omp.Diff.RemovedLine";
        public const string AddedWord = "Omp.Diff.AddedWord";
        public const string RemovedWord = "Omp.Diff.RemovedWord";
    }
}
