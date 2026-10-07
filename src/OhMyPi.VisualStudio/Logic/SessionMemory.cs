namespace OhMyPi.VisualStudio.Logic
{
    /// <summary>The last OMP session file of the working directory OMP runs in now.</summary>
    internal sealed class SessionMemory
    {
        private readonly object _gate = new object();
        private string _cwd = "";
        private string? _last;

        public string? Last
        {
            get { lock (_gate) return _last; }
        }

        /// <summary>Follows a new working directory whose remembered session is <paramref name="last"/>.</summary>
        public void SwitchTo(string cwd, string? last)
        {
            lock (_gate)
            {
                _cwd = cwd;
                _last = string.IsNullOrEmpty(last) ? null : last;
            }
        }

        /// <summary>
        /// Takes note of the session a service running in <paramref name="serviceCwd"/> reported; true when it should be
        /// persisted for <paramref name="serviceCwd"/>. A service still running in a previous directory (it is being
        /// replaced) never changes <see cref="Last"/>, which belongs to the current directory.
        /// </summary>
        public bool Record(string serviceCwd, string? file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            lock (_gate)
            {
                if (!WorkingDirectory.Same(serviceCwd, _cwd)) return true;
                if (file == _last) return false;
                _last = file;
                return true;
            }
        }
    }
}
