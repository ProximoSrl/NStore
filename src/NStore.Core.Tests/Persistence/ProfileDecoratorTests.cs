using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NStore.Core.Persistence;
using Xunit;

namespace NStore.Core.Tests.Persistence
{
    public class ProfileDecoratorTests
    {
        private readonly ProfileDecorator _profile;
        private readonly IPersistence _store;

        public ProfileDecoratorTests()
        {
            var nullStore = new NullPersistence();
            _store = _profile = new ProfileDecorator(nullStore);
        }

        private async Task Wait(int ms)
        {
            await Task.Delay(ms).ConfigureAwait(false);
        }

        [Fact]
        public async Task should_count_wait_time()
        {
            var pi = new TaskProfilingInfo("test");
            var sw = new Stopwatch();
            sw.Start();
            await pi.CaptureAsync(() => Wait(300)).ConfigureAwait(false);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds >= 300, "sw.ElapsedMilliseconds >= 300");
            Assert.True(pi.Elapsed.TotalMilliseconds >= 300, "pi.Elapsed.Milliseconds >= 300");
        }

        [Fact]
        public void counters_should_be_zero()
        {
            Assert.Equal(0, _profile.PersistCounter.Calls);
            Assert.Equal(0, _profile.DeleteCounter.Calls);
            Assert.Equal(0, _profile.StoreScanCounter.Calls);
            Assert.Equal(0, _profile.ReadForwardCounter.Calls);
            Assert.Equal(0, _profile.ReadBackwardCounter.Calls);
            Assert.Equal(0, _profile.ReadSingleBackwardCounter.Calls);
        }

        [Fact]
        public async Task persist_should_be_recorded()
        {
            await _store.AppendAsync("empty", 0, null).ConfigureAwait(false);
            Assert.Equal(1, _profile.PersistCounter.Calls);
            Assert.Equal(0, _profile.DeleteCounter.Calls);
            Assert.Equal(0, _profile.StoreScanCounter.Calls);
            Assert.Equal(0, _profile.ReadForwardCounter.Calls);
            Assert.Equal(0, _profile.ReadBackwardCounter.Calls);
            Assert.Equal(0, _profile.ReadSingleBackwardCounter.Calls);
        }

        [Fact]
        public async Task delete_should_be_recorded()
        {
            await _store.DeleteAsync("empty").ConfigureAwait(false);
            Assert.Equal(0, _profile.PersistCounter.Calls);
            Assert.Equal(1, _profile.DeleteCounter.Calls);
            Assert.Equal(0, _profile.StoreScanCounter.Calls);
            Assert.Equal(0, _profile.ReadForwardCounter.Calls);
            Assert.Equal(0, _profile.ReadBackwardCounter.Calls);
            Assert.Equal(0, _profile.ReadSingleBackwardCounter.Calls);
        }

        [Fact]
        public async Task scan_store_should_be_recorded()
        {
            await _store.ReadAllAsync(0, new AllPartitionsRecorder(), 10).ConfigureAwait(false);
            Assert.Equal(0, _profile.PersistCounter.Calls);
            Assert.Equal(0, _profile.DeleteCounter.Calls);
            Assert.Equal(1, _profile.StoreScanCounter.Calls);
            Assert.Equal(0, _profile.ReadForwardCounter.Calls);
            Assert.Equal(0, _profile.ReadBackwardCounter.Calls);
            Assert.Equal(0, _profile.ReadSingleBackwardCounter.Calls);
        }

        [Fact]
        public async Task scan_partition_should_be_recorded()
        {
            await _store.ReadForwardAsync("empty", 0, new Recorder(), 10).ConfigureAwait(false);
            Assert.Equal(0, _profile.PersistCounter.Calls);
            Assert.Equal(0, _profile.DeleteCounter.Calls);
            Assert.Equal(0, _profile.StoreScanCounter.Calls);
            Assert.Equal(1, _profile.ReadForwardCounter.Calls);
            Assert.Equal(0, _profile.ReadBackwardCounter.Calls);
            Assert.Equal(0, _profile.ReadSingleBackwardCounter.Calls);
        }

        [Fact]
        public async Task scan_partition_backward_should_be_recorded()
        {
            await _store.ReadBackwardAsync("empty", 0, new Recorder(), 10).ConfigureAwait(false);
            Assert.Equal(0, _profile.PersistCounter.Calls);
            Assert.Equal(0, _profile.DeleteCounter.Calls);
            Assert.Equal(0, _profile.StoreScanCounter.Calls);
            Assert.Equal(0, _profile.ReadForwardCounter.Calls);
            Assert.Equal(1, _profile.ReadBackwardCounter.Calls);
            Assert.Equal(0, _profile.ReadSingleBackwardCounter.Calls);
        }

        [Fact]
        public async Task peek_partition_should_be_recorded()
        {
            await _store.ReadSingleBackwardAsync("empty", 1, CancellationToken.None).ConfigureAwait(false);
            Assert.Equal(0, _profile.PersistCounter.Calls);
            Assert.Equal(0, _profile.DeleteCounter.Calls);
            Assert.Equal(0, _profile.StoreScanCounter.Calls);
            Assert.Equal(0, _profile.ReadForwardCounter.Calls);
            Assert.Equal(0, _profile.ReadBackwardCounter.Calls);
            Assert.Equal(1, _profile.ReadSingleBackwardCounter.Calls);
        }
    }
}