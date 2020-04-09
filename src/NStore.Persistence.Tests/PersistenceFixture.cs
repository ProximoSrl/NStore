using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using NStore.Core.Logging;
using NStore.Core.Persistence;
using Xunit;

#pragma warning disable S101 // Types should be named in camel case
#pragma warning disable IDE1006 // Naming Styles

// ReSharper disable InconsistentNaming
namespace NStore.Persistence.Tests
{
    public class TestMisconfiguredException : Exception
    {
        public TestMisconfiguredException(string message) : base(message)
        {
        }
    }

#pragma warning disable S3881 // "IDisposable" should be implemented correctly
    public abstract partial class BasePersistenceTest : IDisposable
#pragma warning restore S3881 // "IDisposable" should be implemented correctly
    {
        private static int _staticId = 0;
        protected readonly int _testRunId;

        protected IPersistence Store { get; }
        protected readonly TestLoggerFactory LoggerFactory;
        protected readonly INStoreLogger _logger;
        protected IEnhancedPersistence Batcher => _persistence as IEnhancedPersistence;
        protected readonly IPersistence _persistence;
        protected BasePersistenceTest()
        {
            _testRunId = Interlocked.Increment(ref _staticId);

            LoggerFactory = new TestLoggerFactory(TestSuitePrefix + "::" + GetType().Name);
            _logger = LoggerFactory.CreateLogger(GetType().FullName);
            _logger.LogDebug("Creating store");
            _persistence = Create(true);
            _logger.LogDebug("Store created");
            Store = new LogDecorator(_persistence, LoggerFactory);
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // dispose managed state (managed objects).
                    Clear();
                    _logger.LogDebug("Test disposed");
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~BasePersistenceTest() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }

    public class WriteTests : BasePersistenceTest
    {
        [Fact]
        public async Task can_insert_at_first_index()
        {
            await Store.AppendAsync("Stream_1", 1, new { data = "this is a test" }).ConfigureAwait(false);
        }
    }

    public class negative_index : BasePersistenceTest
    {
        [Fact]
        public async Task should_persist_with_chunk_id()
        {
            await Store.AppendAsync("Stream_Neg", -1, "payload").ConfigureAwait(false);

            var tape = new Recorder();
            await Store.ReadForwardAsync("Stream_Neg", 0, tape).ConfigureAwait(false);
            Assert.Equal("payload", tape.ByIndex(1).Payload);
        }
    }

    public class insert_at_last_index : BasePersistenceTest
    {
        [Fact]
        public async Task should_work()
        {
            await Store.AppendAsync("Stream_1", long.MaxValue, new { data = "this is a test" }).ConfigureAwait(false);
        }
    }

    public class insert_duplicate_chunk_index : BasePersistenceTest
    {
        [Fact]
        public async Task should_throw()
        {
            await Store.AppendAsync("dup", 1, new { data = "first attempt" }).ConfigureAwait(false);
            await Store.AppendAsync("dup", 2, new { data = "should not work" }).ConfigureAwait(false);

            var ex = await Assert.ThrowsAnyAsync<DuplicateStreamIndexException>(() =>
                Store.AppendAsync("dup", 1, new { data = "this is a test" })
            ).ConfigureAwait(false);

            Assert.Equal("Duplicated index 1 on stream dup", ex.Message);
            Assert.Equal("dup", ex.StreamId);
            Assert.Equal(1, ex.StreamIndex);
        }
    }

    public class query_by_operation_id : BasePersistenceTest
    {
        public query_by_operation_id()
        {
            try
            {
                BuildStream().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                _logger.LogError("Test setup failed: {message}", e.Message);
                throw;
            }
        }

        [Fact]
        public async Task should_return_null_on_missing_operation()
        {
            var chunk = await Store.ReadByOperationIdAsync("stream_1", "nop", CancellationToken.None).ConfigureAwait(false);
            Assert.Null(chunk);
        }

        [Fact]
        public async Task should_return_index_1_for_operation_1()
        {
            var chunk = await Store.ReadByOperationIdAsync("stream_1", "operation_1", CancellationToken.None).ConfigureAwait(false);
            Assert.NotNull(chunk);
            Assert.Equal(1, chunk.Index);
        }

        [Fact]
        public async Task should_return_index_2_for_operation_2()
        {
            var chunk = await Store.ReadByOperationIdAsync("stream_1", "operation_2", CancellationToken.None).ConfigureAwait(false);
            Assert.NotNull(chunk);
            Assert.Equal(2, chunk.Index);
        }

        [Fact]
        public async Task should_find_operation_on_stream_1_2_3()
        {
            var recorder = new Recorder();
            await Store.ReadAllByOperationIdAsync("operation_1", recorder, CancellationToken.None).ConfigureAwait(false);

            Assert.Collection(recorder.Chunks,
                chunk => Assert.Equal("p1", chunk.Payload),
                chunk => Assert.Equal("p3", chunk.Payload),
                chunk => Assert.Equal("p4", chunk.Payload)
            );
        }

        private async Task BuildStream()
        {
            await Store.AppendAsync("stream_1", 1, "p1", "operation_1").ConfigureAwait(false);
            await Store.AppendAsync("stream_1", 2, "p2", "operation_2").ConfigureAwait(false);
            await Store.AppendAsync("stream_2", 1, "p3", "operation_1").ConfigureAwait(false);
            await Store.AppendAsync("stream_3", 1, "p4", "operation_1").ConfigureAwait(false);
            await Store.AppendAsync("stream_4", 1, "p5", "operation_2").ConfigureAwait(false);
        }
    }

    public class ScanTest : BasePersistenceTest
    {
        public ScanTest()
        {
            try
            {
                Store.AppendAsync("Stream_1", 1, "a").ConfigureAwait(false).GetAwaiter().GetResult();
                Store.AppendAsync("Stream_1", 2, "b").ConfigureAwait(false).GetAwaiter().GetResult();
                Store.AppendAsync("Stream_1", 3, "c").ConfigureAwait(false).GetAwaiter().GetResult();

                Store.AppendAsync("Stream_2", 1, "d").ConfigureAwait(false).GetAwaiter().GetResult();
                Store.AppendAsync("Stream_2", 2, "e").ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                _logger.LogError("Scan test setup failed: {message}", e.Message);
                throw;
            }

            _logger.LogDebug("Scan test data written");
        }

        [Fact]
        public async Task ReadFirst()
        {
            object payload = null;

            await Store.ReadForwardAsync(
                "Stream_1", 0, new LambdaSubscription(data =>
                {
                    payload = data.Payload;
                    return Task.FromResult(false);
                })
            ).ConfigureAwait(false);

            Assert.Equal("a", payload);
        }

        [Fact]
        public async Task should_read_last_of_partition()
        {
            object payload = null;

            await Store.ReadBackwardAsync(
                "Stream_1",
                long.MaxValue,
                new LambdaSubscription(data =>
                {
                    payload = data.Payload;
                    return Task.FromResult(false);
                })
            ).ConfigureAwait(false);

            Assert.Equal("c", payload);
        }

        [Fact]
        public async Task should_read_only_first_two_chunks()
        {
            var recorder = new Recorder();

            await Store.ReadForwardAsync(
                "Stream_1", 0, recorder, 2
            ).ConfigureAwait(false);

            Assert.Equal(2, recorder.Length);
            Assert.Equal("a", recorder[0].Payload);
            Assert.Equal("b", recorder[1].Payload);
        }

        [Fact]
        public async Task read_forward_should_call_complete_on_consumer()
        {
            var recorder = new Recorder();

            await Store.ReadForwardAsync(
                "Stream_1", 0, recorder, 2
            ).ConfigureAwait(false);

            Assert.True(recorder.ReadCompleted);
        }

        [Fact]
        public async Task read_backward_should_call_complete_on_consumer()
        {
            var recorder = new Recorder();

            await Store.ReadBackwardAsync(
                "Stream_1", 2, recorder, 0
            ).ConfigureAwait(false);

            Assert.True(recorder.ReadCompleted);
        }


        [Fact]
        public async Task should_read_only_last_two_chunks()
        {
            var tape = new Recorder();

            await Store.ReadBackwardAsync(
                "Stream_1",
                3,
                tape,
                2
            ).ConfigureAwait(false);

            Assert.Equal(2, tape.Length);
            Assert.Equal("c", tape[0].Payload);
            Assert.Equal("b", tape[1].Payload);
        }

        [Fact]
        public async Task read_all_forward()
        {
            var tape = new AllPartitionsRecorder();
            await Store.ReadAllAsync(0, tape).ConfigureAwait(false);

            Assert.Equal(5, tape.Length);
            Assert.Equal("a", tape[0]);
            Assert.Equal("b", tape[1]);
            Assert.Equal("c", tape[2]);
            Assert.Equal("d", tape[3]);
            Assert.Equal("e", tape[4]);
        }

        [Fact]
        public async Task read_all_forward_from_middle()
        {
            var tape = new AllPartitionsRecorder();
            await Store.ReadAllAsync(3, tape).ConfigureAwait(false);

            Assert.Equal(3, tape.Length);
            Assert.Equal("c", tape[0]);
            Assert.Equal("d", tape[1]);
            Assert.Equal("e", tape[2]);
        }

        [Fact]
        public async Task read_all_forward_from_middle_limit_one()
        {
            var tape = new AllPartitionsRecorder();
            await Store.ReadAllAsync(3, tape, 1).ConfigureAwait(false);

            Assert.Equal(1, tape.Length);
            Assert.Equal("c", tape[0]);
        }
    }

    public class read_last_position : BasePersistenceTest
    {
        [Fact]
        public async Task on_empty_store_should_be_equal_zero()
        {
            var last = await Store.ReadLastPositionAsync().ConfigureAwait(false);
            Assert.Equal(0L, last);
        }

        [Fact]
        public async Task with_only_one_chunk_should_be_equal_to_one()
        {
            await Store.AppendAsync("a", -1, "last").ConfigureAwait(false);
            var last = await Store.ReadLastPositionAsync().ConfigureAwait(false);
            Assert.Equal(1L, last);
        }

        [Fact]
        public async Task with_two_streams_of_one_chunk_should_be_two()
        {
            await Store.AppendAsync("a", "first").ConfigureAwait(false);
            await Store.AppendAsync("b", "second").ConfigureAwait(false);
            var last = await Store.ReadLastPositionAsync().ConfigureAwait(false);
            Assert.Equal(2L, last);
        }
    }

    public class ByteArrayPersistenceTest : BasePersistenceTest
    {
        [Fact]
        public async Task InsertByteArray()
        {
            await Store.AppendAsync("BA", 0, System.Text.Encoding.UTF8.GetBytes("this is a test")).ConfigureAwait(false);

            byte[] payload = null;
            await Store.ReadForwardAsync("BA", 0, new LambdaSubscription(data =>
            {
                payload = (byte[])data.Payload;
                return Task.FromResult(true);
            })).ConfigureAwait(false);

            var text = System.Text.Encoding.UTF8.GetString(payload);
            Assert.Equal("this is a test", text);
        }
    }

    public class IdempotencyTest : BasePersistenceTest
    {
        [Fact]
        public async Task cannot_append_same_operation_twice_on_same_stream()
        {
            var opId = "operation_1";
            await Store.AppendAsync("Id_1", 0, new { data = "this is a test" }, opId).ConfigureAwait(false);
            await Store.AppendAsync("Id_1", 1, new { data = "this is a test" }, opId).ConfigureAwait(false);

            var list = new List<long>();
            await Store.ReadForwardAsync("Id_1", 0, new LambdaSubscription(data =>
            {
                list.Add(data.Index);
                return Task.FromResult(true);
            })).ConfigureAwait(false);

            Assert.True(1 == list.Count);
            Assert.Equal(0, list[0]);
        }

        [Fact]
        public async Task can_append_same_operation_to_two_streams()
        {
            var opId = "operation_2";
            await Store.AppendAsync("Id_1", 0, "a", opId).ConfigureAwait(false);
            await Store.AppendAsync("Id_2", 1, "b", opId).ConfigureAwait(false);

            var list = new List<object>();
            await Store.ReadForwardAsync("Id_1", 0, new LambdaSubscription(data =>
            {
                list.Add(data.Payload);
                return Task.FromResult(true);
            })).ConfigureAwait(false);

            await Store.ReadForwardAsync("Id_2", 0, new LambdaSubscription(data =>
            {
                list.Add(data.Payload);
                return Task.FromResult(true);
            })).ConfigureAwait(false);

            Assert.Equal(2, list.Count);
        }
    }

    public class DeleteStreamTest : BasePersistenceTest
    {
        protected DeleteStreamTest()
        {
            try
            {
                Task.WhenAll
                (
                    Store.AppendAsync("delete", 1, null),
                    Store.AppendAsync("delete_3", 1, "1"),
                    Store.AppendAsync("delete_3", 2, "2"),
                    Store.AppendAsync("delete_3", 3, "3"),
                    Store.AppendAsync("delete_4", 1, "1"),
                    Store.AppendAsync("delete_4", 2, "2"),
                    Store.AppendAsync("delete_4", 3, "3"),
                    Store.AppendAsync("delete_5", 1, "1"),
                    Store.AppendAsync("delete_5", 2, "2"),
                    Store.AppendAsync("delete_5", 3, "3")
                ).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                _logger.LogError("Delete stream test setup: {message}", e.Message);
                throw;
            }

            _logger.LogDebug("Delete test data written");
        }

    }

    public class DeleteStreamTest_1 : DeleteStreamTest
    {
        [Fact]
        public async Task delete_stream()
        {
            await Store.DeleteAsync("delete").ConfigureAwait(false);
            bool almostOneChunk = false;
            await Store.ReadForwardAsync("delete", 0, new LambdaSubscription(data =>
            {
                almostOneChunk = true;
                return Task.FromResult(false);
            })).ConfigureAwait(false);

            Assert.False(almostOneChunk, "Should not contains chunks");
        }
    }

    public class DeleteStreamTest_2 : DeleteStreamTest
    {

		[Fact]
		public async Task delete_invalid_stream_should_not_throw_exception()
		{
			await Store.DeleteAsync("delete_2").ConfigureAwait(false);
		}
	}

    public class DeleteStreamTest_3 : DeleteStreamTest
    {
        [Fact]
        public async Task should_delete_first()
        {
            _logger.LogDebug("deleting first chunk");
            await Store.DeleteAsync("delete_3", 1, 1).ConfigureAwait(false);
            _logger.LogDebug("recording");
            var acc = new Recorder();
            await Store.ReadForwardAsync("delete_3", 0, acc).ConfigureAwait(false);

            _logger.LogDebug("checking assertions");
            Assert.Equal(2, acc.Length);
            Assert.True((string)acc[0].Payload == "2");
            Assert.True((string)acc[1].Payload == "3");
            _logger.LogDebug("done");
        }
    }

    public class DeleteStreamTest_4 : DeleteStreamTest
    {
        [Fact]
        public async Task should_delete_last()
        {
            await Store.DeleteAsync("delete_4", 3).ConfigureAwait(false);
            var acc = new Recorder();
            await Store.ReadForwardAsync("delete_4", 0, acc).ConfigureAwait(false);

            Assert.Equal(2, acc.Length);
            Assert.True((string)acc[0].Payload == "1");
            Assert.True((string)acc[1].Payload == "2");
        }
    }

    public class DeleteStreamTest_5 : DeleteStreamTest
    {

        [Fact]
        public async Task should_delete_middle()
        {
            await Store.DeleteAsync("delete_5", 2, 2).ConfigureAwait(false);
            var acc = new Recorder();
            await Store.ReadForwardAsync("delete_5", 0, acc).ConfigureAwait(false);

            Assert.Equal(2, acc.Length);
            Assert.True((string)acc[0].Payload == "1");
            Assert.True((string)acc[1].Payload == "3");
        }
    }

    public class deleted_chunks_management : BasePersistenceTest
    {
        [Fact]
        public async Task deleted_chunks_should_be_hidden_from_scan()
        {
            await Store.AppendAsync("a", 1, "first").ConfigureAwait(false);
            await Store.AppendAsync("a", 2, "second").ConfigureAwait(false);
            await Store.AppendAsync("a", 3, "third").ConfigureAwait(false);

            await Store.DeleteAsync("a", 2, 2).ConfigureAwait(false);

            var recorder = new AllPartitionsRecorder();
            await Store.ReadAllAsync(0, recorder).ConfigureAwait(false);

            Assert.Equal(2, recorder.Length);
            Assert.Equal("first", recorder[0]);
            Assert.Equal("third", recorder[1]);
        }

        [Fact]
        public async Task deleted_chunks_should_be_hidden_from_peek()
        {
            await Store.AppendAsync("a", 1, "first").ConfigureAwait(false);
            await Store.AppendAsync("a", 2, "second").ConfigureAwait(false);

            await Store.DeleteAsync("a", 2, 2).ConfigureAwait(false);

            var chunk = await Store.ReadSingleBackwardAsync("a", 100, CancellationToken.None).ConfigureAwait(false);

            Assert.NotNull(chunk);
            Assert.Equal("first", chunk.Payload);
        }

        [Theory]
        [InlineData(1, 3)]
        [InlineData(2, 3)]
        //		[InlineData(3, 3)] @@TODO enable tombstone!
        public async Task poller_should_skip_missing_chunks(long missing, long expected)
        {
            await Store.AppendAsync("a", 1, "1").ConfigureAwait(false);
            await Store.AppendAsync("a", 2, "2").ConfigureAwait(false);
            await Store.AppendAsync("a", 3, "3").ConfigureAwait(false);

            await Store.DeleteAsync("a", missing, missing).ConfigureAwait(false);

            var recored = new AllPartitionsRecorder();
            var poller = new PollingClient(Store, 0, recored, this.LoggerFactory)
            {
                HoleDetectionTimeout = 100
            };

            var cts = new CancellationTokenSource(20000);

            await poller.Poll(cts.Token).ConfigureAwait(false);
            await poller.Poll(cts.Token).ConfigureAwait(false);
            Assert.Equal(expected, poller.Position);
        }
    }

    public class subscription_events_should_be_signaled : BasePersistenceTest
    {
        private readonly ChunkProcessor _continueToEnd = c => Task.FromResult(true);
        private readonly LambdaSubscription _subscription;
        private long _startedAt = -1;
        private long _completedAt = -1;

        public subscription_events_should_be_signaled()
        {
            // write stream out of order to have 
            // position 1 : index 2 
            // position 2 : index 1
            //
            Store.AppendAsync("a", 2, "data b").GetAwaiter().GetResult();
            Store.AppendAsync("a", 1, "data a").GetAwaiter().GetResult();

            _subscription = new LambdaSubscription(_continueToEnd)
            {
                OnStart = p =>
                {
                    _startedAt = p;
                    return Task.CompletedTask;
                },
                OnComplete = p =>
                {
                    _completedAt = p;
                    return Task.CompletedTask;
                }
            };
        }

        [Fact]
        public async Task on_read_all()
        {
            await Store.ReadAllAsync(fromPositionInclusive: 1, subscription: _subscription).ConfigureAwait(false);
            Assert.True(1 == _startedAt, "start position " + _startedAt);
            Assert.True(2 == _completedAt, "complete position " + _completedAt);
        }

        [Fact]
        public async Task on_read_forward()
        {
            await Store.ReadForwardAsync("a", fromLowerIndexInclusive: 1, subscription: _subscription).ConfigureAwait(false);
            Assert.True(1 == _startedAt, _testRunId + ": start position " + _startedAt);
            Assert.True(2 == _completedAt, _testRunId + ": complete position " + _completedAt);
        }

        [Fact]
        public async Task on_read_backward()
        {
            await Store.ReadBackwardAsync("a", fromUpperIndexInclusive: 100, subscription: _subscription).ConfigureAwait(false);
            Assert.True(100 == _startedAt, _testRunId + ": start position " + _startedAt);
            Assert.True(1 == _completedAt, _testRunId + ": complete position " + _completedAt);
        }
    }

    public class exceptions_should_be_signaled : BasePersistenceTest
    {
        private readonly ChunkProcessor _throw = _ => throw new TimeoutException();
        private readonly LambdaSubscription _subscription;

        public exceptions_should_be_signaled()
        {
            // write stream out of order to have 
            // position 1 : index 2 
            // position 2 : index 1
            //
            Store.AppendAsync("a", 2, "data b").GetAwaiter().GetResult();
            Store.AppendAsync("a", 1, "data a").GetAwaiter().GetResult();

            _subscription = new LambdaSubscription(_throw);
        }

        [Fact]
        public async Task on_read_all()
        {
            await Store.ReadAllAsync(0, _subscription).ConfigureAwait(false);
            Assert.True(_subscription.Failed);
        }

        [Fact]
        public async Task on_read_forward()
        {
            await Store.ReadForwardAsync("a", 0, _subscription).ConfigureAwait(false);
            Assert.True(_subscription.Failed);
        }

        [Fact]
        public async Task on_read_backward()
        {
            await Store.ReadBackwardAsync("a", 100, _subscription).ConfigureAwait(false);
            Assert.True(_subscription.Failed);
        }
    }


    public class strict_sequence_on_store : BasePersistenceTest
    {
        [Fact]
        public async Task on_concurrency_exception_holes_are_filled_with_empty_chunks()
        {
            if (!Store.SupportsFillers)
            {
                return;
            }

            var exceptions = 0;
            var writers = Enumerable.Range(1, 400).Select(async i =>
                {
                    try
                    {
                        await Store.AppendAsync("collision_wanted", 1 + i % 5, "payload").ConfigureAwait(false);
                    }
                    catch (DuplicateStreamIndexException)
                    {
                        Interlocked.Increment(ref exceptions);
                    }
                }
            ).ToArray();

            await Task.WhenAll(writers).ConfigureAwait(false);

            Assert.True(exceptions > 0);
            var recorder = new Recorder();
            await Store.ReadForwardAsync("::empty", 0, recorder).ConfigureAwait(false);

            Assert.Equal(exceptions, recorder.Length);
        }
    }

    public class polling_client_tests : BasePersistenceTest
    {
        [Theory]
        [InlineData(0, 3)]
        [InlineData(1, 2)]
        [InlineData(2, 1)]
        [InlineData(3, 0)]
        [InlineData(4, 0)]
        public async Task should_read_from_position(long start, long expected)
        {
            await Store.AppendAsync("a", 1, "1").ConfigureAwait(false);
            await Store.AppendAsync("a", 2, "2").ConfigureAwait(false);
            await Store.AppendAsync("a", 3, "3").ConfigureAwait(false);

            var recorder = new AllPartitionsRecorder();
            var client = new PollingClient(Store, start, recorder, LoggerFactory);

            await client.Poll(5000).ConfigureAwait(false);

            Assert.Equal(expected, recorder.Length);
        }
    }

    public class large_payload_tests : BasePersistenceTest
    {
        [Fact]
        public async Task can_write_and_read_large_payload()
        {
            var payload = new byte[1_000_000];
            for (int c = 0; c < payload.Length; c++)
            {
                payload[c] = Convert.ToByte(c % 255);
            }

            await _persistence.AppendAsync("large_binary", payload).ConfigureAwait(false);

            var chunk = await _persistence.ReadSingleBackwardAsync("large_binary").ConfigureAwait(false);
            var loadedBytes = (byte[])chunk.Payload;

            Assert.Equal(payload, loadedBytes);
        }
    }

    public class concurrency_test : BasePersistenceTest
    {
        [Theory]
        [InlineData(1, false)]
        [InlineData(8, false)]
        [InlineData(1, true)]
        [InlineData(8, true)]
        public async Task polling_client_should_not_miss_data(int parallelism, bool autopolling)
        {
            _logger.LogDebug("Starting with {Parallelism} workers and Autopolling {Autopolling}", parallelism, autopolling);

            var sequenceChecker = new StrictSequenceChecker($"Workers {parallelism} autopolling {autopolling}");
            var poller = new PollingClient(Store, 0, sequenceChecker, this.LoggerFactory)
            {
                PollingIntervalMilliseconds = 0,
                HoleDetectionTimeout = 1000
            };

            if (autopolling)
            {
                poller.Start();
                _logger.LogDebug("Started Polling");
            }

            const int range = 1000;

            var producer = new ActionBlock<int>(async i =>
            {
                await Store.AppendAsync("p", i, "demo", "op#" + i).ConfigureAwait(false);
            }, new ExecutionDataflowBlockOptions()
            {
                MaxDegreeOfParallelism = parallelism
            });

            _logger.LogDebug("Started pushing data: {elements} elements", range);

            foreach (var i in Enumerable.Range(1, range))
            {
                Assert.True(await producer.SendAsync(i).ConfigureAwait(false));
            }

            producer.Complete();
            await producer.Completion.ConfigureAwait(false);
            _logger.LogDebug("Data pushed");

            if (autopolling)
            {
                _logger.LogDebug("Stopping poller");
                await poller.Stop().ConfigureAwait(false);
                _logger.LogDebug("Poller stopped");
            }

            // read to end
            _logger.LogDebug("Polling to end");
            var timeout = new CancellationTokenSource(60000);
            await poller.Poll(timeout.Token).ConfigureAwait(false);
            _logger.LogDebug("Polling to end - done");

            Assert.True(poller.Position == sequenceChecker.Position, "Sequence " + sequenceChecker.Position + " != Position " + poller.Position);
            Assert.True(range == poller.Position, "Poller @" + poller.Position);
            Assert.True(range == sequenceChecker.Position, "Sequence @" + sequenceChecker.Position);
        }
    }

    public class StrictSequenceChecker : ISubscription
    {
        private int _expectedPosition = 1;
        private readonly string _configMessage;

        public StrictSequenceChecker(string configMessage)
        {
            this._configMessage = configMessage;
        }

        public int Position => _expectedPosition - 1;

        public Task OnStartAsync(long indexOrPosition)
        {
            return Task.CompletedTask;
        }

        public Task<bool> OnNextAsync(IChunk chunk)
        {
            if (_expectedPosition != chunk.Position)
            {
                throw new Exception($"Expected position {_expectedPosition} got {chunk.Position} | {_configMessage}");
            }

            _expectedPosition++;
            return Task.FromResult(true);
        }

        public Task CompletedAsync(long indexOrPosition)
        {
            return Task.CompletedTask;
        }

        public Task StoppedAsync(long indexOrPosition)
        {
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(long indexOrPosition, Exception ex)
        {
            Console.WriteLine($"Error [{_configMessage}]: {ex.Message}\n{ex.StackTrace}");
            throw ex;
        }
    }
}