using System.Linq;
using Omp.Core;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Tests
{
    public class PickerListTests
    {
        private static SessionSummary Session(string path, string title) => new SessionSummary { Path = path, Title = title };

        [Fact]
        public void Recent_sessions_exclude_current_and_cap_at_five()
        {
            var all = Enumerable.Range(1, 8).Select(i => Session($"s{i}.jsonl", $"S{i}")).ToArray();
            var recent = SessionList.Recent(all, "s2.jsonl");
            Assert.Equal(new[] { "s1.jsonl", "s3.jsonl", "s4.jsonl", "s5.jsonl", "s6.jsonl" }, recent.Sessions.Select(s => s.Path));
            Assert.Equal(7, recent.Total);
        }

        [Fact]
        public void Session_search_matches_title_and_cwd()
        {
            var all = new[] { Session("a.jsonl", "Fix auth"), new SessionSummary { Path = "b.jsonl", Title = "Other", Cwd = "D:\\dev\\auth-service" }, Session("c.jsonl", "Docs") };
            Assert.Equal(new[] { "a.jsonl", "b.jsonl" }, SessionList.Filter(all, "auth", s => s.Title ?? "").Select(s => s.Path));
            Assert.Equal(3, SessionList.Filter(all, "  ", s => s.Title ?? "").Count);
        }
    }
}
