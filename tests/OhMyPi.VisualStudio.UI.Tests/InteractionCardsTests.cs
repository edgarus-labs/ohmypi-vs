using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using Omp.Core;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests
{
    /// <summary>What each kind of OMP request card sends back.</summary>
    [Collection("wpf")]
    public class InteractionCardsTests
    {
        private sealed class UnknownRequest : InteractionRequest
        {
        }

        private static Button Visible(System.Windows.DependencyObject root, string text) =>
            Descendants(root).OfType<Button>().Last(b => b.IsVisible && b.Content is string s && s == text);

        private static void Show(FakeService service, InteractionRequest request)
        {
            service.RaiseInteraction(request);
            Pump(200);
        }

        [Fact]
        public void A_select_card_answers_with_the_clicked_option_or_cancels()
        {
            var service = new FakeService();
            RunSta((window, control) =>
            {
                Show(service, new SelectRequest { Id = "s1", Title = "Pick a branch", Options = new[] { new SelectOptionView { Label = "main", Description = "default" }, new SelectOptionView { Label = "dev" } } });
                Click(Named<Button>(window, "dev"));
                Pump();
                Show(service, new SelectRequest { Id = "s2", Title = "Pick again", Options = new[] { new SelectOptionView { Label = "x" } } });
                Click(Visible(window, "Cancel"));
                Pump();
                Assert.Equal("dev", Assert.IsType<ValueResponse>(service.Responses[0].Response).Value);
                Assert.Equal("s2", service.Responses[1].Id);
                Assert.IsType<CancelledResponse>(service.Responses[1].Response);
            }, service, new FakeHost());
        }

        [Fact]
        public void An_editor_card_submits_the_edited_text()
        {
            var service = new FakeService();
            RunSta((window, control) =>
            {
                Show(service, new EditorRequest { Id = "e1", Title = "Edit plan", Prefill = "step 1" });
                var area = Named<TextBox>(window, "Edit plan");
                Assert.Equal("step 1", area.Text);
                area.Text = "step 1\nstep 2";
                Click(Visible(window, "Submit"));
                Pump();
                Assert.Equal("step 1\nstep 2", Assert.IsType<ValueResponse>(Assert.Single(service.Responses).Response).Value);
            }, service, new FakeHost());
        }

        [Fact]
        public void An_input_card_submits_on_enter_and_a_secret_one_reads_the_password_box()
        {
            var service = new FakeService();
            RunSta((window, control) =>
            {
                Show(service, new InputRequest { Id = "i1", Title = "Name", Placeholder = "your name" });
                Assert.True(HasText(window, "your name"));
                var box = Named<TextBox>(window, "Name");
                box.Text = "Ada";
                Press(box, Key.A);
                Press(box, Key.Enter);
                Pump();

                Show(service, new InputRequest { Id = "i2", Title = "Token", Secret = true });
                var secret = Named<PasswordBox>(window, "Token");
                secret.Password = "s3cret";
                Click(Visible(window, "Submit"));
                Pump();

                Assert.Equal(new[] { "Ada", "s3cret" }, service.Responses.Select(r => ((ValueResponse)r.Response).Value));
            }, service, new FakeHost());
        }

        [Fact]
        public void An_ask_card_answers_every_question_with_choices_or_typed_text()
        {
            var service = new FakeService();
            RunSta((window, control) =>
            {
                Show(service, new AskRequest
                {
                    Id = "a1",
                    Questions = new[]
                    {
                        new AskQuestionView { Id = "db", Header = "Storage", Question = "Database?", Recommended = 0, Options = new[] { new AskOptionView { Label = "SQLite", Description = "file", Preview = "local" }, new AskOptionView { Label = "Postgres" } } },
                        new AskQuestionView { Id = "os", Question = "Targets?", Multi = true, Options = new[] { new AskOptionView { Label = "Windows" }, new AskOptionView { Label = "Linux" } } },
                    },
                });
                Assert.True(HasText(window, "2 questions"));
                Assert.True(HasText(window, "recommended"));
                Named<RadioButton>(window, "SQLite").IsChecked = true;
                var other = Named<TextBox>(window, "Database? — other");
                other.Text = "   ";
                Assert.True(Named<RadioButton>(window, "SQLite").IsChecked);
                other.Text = "DuckDB";
                Assert.False(Named<RadioButton>(window, "SQLite").IsChecked);
                Named<RadioButton>(window, "Postgres").IsChecked = true;
                Assert.Equal("", other.Text);
                Named<CheckBox>(window, "Windows").IsChecked = true;
                Named<CheckBox>(window, "Linux").IsChecked = true;
                Click(Visible(window, "Submit"));
                Pump();

                var answers = Assert.IsType<AnswersResponse>(Assert.Single(service.Responses).Response).Answers;
                Assert.Equal(new[] { "Postgres" }, answers[0].SelectedOptions);
                Assert.Equal(new[] { "Windows", "Linux" }, answers[1].SelectedOptions);
            }, service, new FakeHost());
        }

        [Fact]
        public void A_single_question_is_titled_question_and_an_unknown_request_can_only_be_cancelled()
        {
            var service = new FakeService();
            RunSta((window, control) =>
            {
                Show(service, new AskRequest { Id = "q", Questions = new[] { new AskQuestionView { Id = "x", Question = "Proceed?", Options = new[] { new AskOptionView { Label = "Yes" } } } } });
                Assert.NotNull(Named<System.Windows.Controls.Border>(window, "Question"));
                Click(Visible(window, "Cancel"));
                Pump();

                Show(service, new UnknownRequest { Id = "u" });
                Assert.NotNull(Named<System.Windows.Controls.Border>(window, "OMP request"));
                Click(Visible(window, "Cancel"));
                Pump();
                Assert.Equal(new[] { "q", "u" }, service.Responses.Select(r => r.Id));
            }, service, new FakeHost());
        }

        [Fact]
        public void A_confirm_card_without_a_message_still_offers_approve_and_a_timed_card_counts_down()
        {
            var service = new FakeService();
            RunSta((window, control) =>
            {
                Show(service, new ConfirmRequest { Id = "c", Title = "Run it?", TimeoutMs = 30_000 });
                Assert.True(HasText(window, "30s left") || HasText(window, "29s left"));
                Click(Visible(window, "Approve"));
                Pump();
                Assert.True(Assert.IsType<ConfirmedResponse>(Assert.Single(service.Responses).Response).Confirmed);
                Assert.False(HasText(window, "s left"));
            }, service, new FakeHost());
        }
    }
}
