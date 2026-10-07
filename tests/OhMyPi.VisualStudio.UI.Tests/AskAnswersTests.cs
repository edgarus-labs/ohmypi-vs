using System.Linq;
using Omp.Core;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Tests
{
    public class AskAnswersTests
    {
        private static readonly AskQuestionView Db = new AskQuestionView
        {
            Id = "db",
            Question = "Which database?",
            Options = new[] { new AskOptionView { Label = "Postgres" }, new AskOptionView { Label = "SQLite" } },
            Recommended = 1,
        };

        private static readonly AskQuestionView Features = new AskQuestionView
        {
            Id = "features",
            Question = "Which features?",
            Options = new[] { new AskOptionView { Label = "Auth" }, new AskOptionView { Label = "Billing" }, new AskOptionView { Label = "Search" } },
            Multi = true,
        };

        private static string Show(AskAnswer a) => $"{a.Id}:[{string.Join(",", a.SelectedOptions)}]{(a.CustomInput == null ? "" : "+" + a.CustomInput)}";

        [Fact]
        public void Matches_rpc_example_shape()
        {
            var answers = AskAnswers.Build(new[] { Db, Features }, new[] { new AskQuestionState(new string[0], "  DuckDB "), new AskQuestionState(new[] { "Search", "Auth" }, "") });
            Assert.Equal(new[] { "db:[]+DuckDB", "features:[Auth,Search]" }, answers.Select(Show));
        }

        [Fact]
        public void Single_select_keeps_one_option_and_drops_it_for_free_text()
        {
            Assert.Equal("db:[SQLite]", Show(AskAnswers.Build(new[] { Db }, new[] { new AskQuestionState(new[] { "SQLite", "Postgres" }, "") }).Single()));
            Assert.Equal("db:[]+Mongo", Show(AskAnswers.Build(new[] { Db }, new[] { new AskQuestionState(new[] { "SQLite" }, "Mongo") }).Single()));
        }

        [Fact]
        public void Multi_select_dedupes_drops_unknown_keeps_option_order()
        {
            Assert.Equal("features:[Auth,Search]+extra", Show(AskAnswers.Build(new[] { Features }, new[] { new AskQuestionState(new[] { "Search", "Nope", "Auth", "Search" }, " extra ") }).Single()));
            Assert.Equal("features:[]", Show(AskAnswers.Build(new[] { Features }, new[] { new AskQuestionState(new string[0], "   ") }).Single()));
        }

        [Fact]
        public void One_entry_per_question_even_without_state()
        {
            Assert.Equal(new[] { "db:[]", "features:[]" }, AskAnswers.Build(new[] { Db, Features }, new AskQuestionState[0]).Select(Show));
        }
    }
}
