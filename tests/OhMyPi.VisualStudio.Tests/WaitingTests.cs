using System;
using System.Threading;
using System.Threading.Tasks;
using OhMyPi.VisualStudio.Logic.Automation;

namespace OhMyPi.VisualStudio.Tests
{
    public class WaitingTests
    {
        [Fact]
        public async Task ReturnsTrueWhenTheTaskFinishesFirst()
        {
            var finished = await Waiting.WaitAsync(Task.CompletedTask, TimeSpan.FromSeconds(30), CancellationToken.None);

            Assert.True(finished);
        }

        [Fact]
        public async Task ReturnsTrueWhenTheTaskFinishesDuringTheWait()
        {
            var source = new TaskCompletionSource<bool>();
            var wait = Waiting.WaitAsync(source.Task, TimeSpan.FromSeconds(30), CancellationToken.None);
            source.SetResult(true);

            Assert.True(await wait);
        }

        [Fact]
        public async Task ReturnsFalseWhenTheTimeoutElapsesFirst()
        {
            var never = new TaskCompletionSource<bool>();

            var finished = await Waiting.WaitAsync(never.Task, TimeSpan.FromMilliseconds(20), CancellationToken.None);

            Assert.False(finished);
        }

        [Fact]
        public async Task ThrowsWhenTheCallerCancelsDuringTheWait()
        {
            var never = new TaskCompletionSource<bool>();
            using (var cts = new CancellationTokenSource())
            {
                var wait = Waiting.WaitAsync(never.Task, TimeSpan.FromSeconds(30), cts.Token);
                cts.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
            }
        }

        [Fact]
        public async Task ThrowsWhenTheTokenIsAlreadyCancelled()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => Waiting.WaitAsync(Task.CompletedTask, TimeSpan.FromSeconds(30), cts.Token));
            }
        }
    }
}
