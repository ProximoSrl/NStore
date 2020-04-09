using NStore.Core.Persistence;
using NStore.Core.Snapshots;
using System;
using System.Threading.Tasks;
using Xunit;

namespace NStore.Persistence.Tests
{
    public class State
    {
        public State()
        {
        }

        public State(State source)
        {
        }
    }

    public class DefaultSnapshotStoreTests : BasePersistenceTest
    {
        private readonly ISnapshotStore _snapshots;

        public DefaultSnapshotStoreTests()
        {
            _snapshots = new DefaultSnapshotStore(Store);
        }

        [Fact]
        public async Task loading_missing_snapshot_should_return_empty()
        {
            var snapshot = await _snapshots.GetAsync("no-one", 1).ConfigureAwait(false);
            Assert.Null(snapshot);
        }

        [Fact]
        public async Task empty_snapshot_is_not_persisted()
        {
            var nullSnapshot = new SnapshotInfo("empty", 0, null, string.Empty);
            await _snapshots.AddAsync("empty", nullSnapshot).ConfigureAwait(false);

            var tape = new Recorder();
            await Store.ReadForwardAsync("empty", 0, tape).ConfigureAwait(false);

            Assert.True(tape.IsEmpty);
        }

        [Fact]
        public async Task snapshot_data_should_be_different_across_write_and_reads()
        {
            var input = new SnapshotInfo("Aggregate_1", 1, new State(), string.Empty);

            await _snapshots.AddAsync("Aggregate_1", input).ConfigureAwait(false);
            var output = await _snapshots.GetAsync("Aggregate_1", Int32.MaxValue).ConfigureAwait(false);

            Assert.NotSame(input.Payload, output.Payload);
        }

        [Fact]
        public async Task snapshots_can_be_deleted()
        {
            var input = new SnapshotInfo("Aggregate_1", 1, new State(), string.Empty);
            await _snapshots.AddAsync("Aggregate_1", input).ConfigureAwait(false);

            await _snapshots.DeleteAsync("Aggregate_1").ConfigureAwait(false);

            var tape = new Recorder();
            await Store.ReadForwardAsync("Aggregate_1", 0, tape).ConfigureAwait(false);

            Assert.True(tape.IsEmpty);
        }
    }
}