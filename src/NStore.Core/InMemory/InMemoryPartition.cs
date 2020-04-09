using NStore.Core.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NStore.Core.InMemory
{
    internal class InMemoryPartition
    {
        private readonly ReaderWriterLockSlim _lockSlim = new ReaderWriterLockSlim();

        public InMemoryPartition(string partitionId, INetworkSimulator networkSimulator, Func<MemoryChunk, MemoryChunk> clone)
        {
            this.Id = partitionId;
            _networkSimulator = networkSimulator;
            Clone = clone;
        }

        private Func<MemoryChunk, MemoryChunk> Clone { get; }
        public string Id { get; set; }
        private IEnumerable<MemoryChunk> Chunks => _sortedChunks.Values;

        private readonly SortedDictionary<long, MemoryChunk> _sortedChunks =
            new SortedDictionary<long, MemoryChunk>();

        private readonly IDictionary<string, MemoryChunk> _operations = new Dictionary<string, MemoryChunk>();
        private readonly INetworkSimulator _networkSimulator;

        public async Task ReadForward(
            long fromLowerIndexInclusive,
            ISubscription subscription,
            long toUpperIndexInclusive,
            int limit,
            CancellationToken cancellationToken)
        {
            _lockSlim.EnterReadLock();

            var result = Chunks
                .Where(x => x.Index >= fromLowerIndexInclusive && x.Index <= toUpperIndexInclusive)
                .Take(limit)
                .ToArray();

            _lockSlim.ExitReadLock();
            await PushToSubscriber(fromLowerIndexInclusive, subscription, result, cancellationToken).ConfigureAwait(false);
        }

        public Task ReadBackward(
            long fromUpperIndexInclusive,
            ISubscription subscription,
            long toLowerIndexInclusive,
            int limit,
            CancellationToken cancellationToken
        )
        {
            _lockSlim.EnterReadLock();

            var result = Chunks.Reverse()
                .Where(x => x.Index <= fromUpperIndexInclusive && x.Index >= toLowerIndexInclusive)
                .Take(limit)
                .ToArray();

            _lockSlim.ExitReadLock();

            return PushToSubscriber(fromUpperIndexInclusive, subscription, result, cancellationToken);
        }

        public Task<IChunk> Peek(long maxValue, CancellationToken cancellationToken)
        {
            _lockSlim.EnterReadLock();

            var chunk = Chunks.Reverse()
                .Where(x => x.Index <= maxValue)
                .Take(1)
                .SingleOrDefault();

            _lockSlim.ExitReadLock();

            return Task.FromResult((IChunk)Clone(chunk));
        }

        private async Task PushToSubscriber(
            long start,
            ISubscription subscription,
            IEnumerable<MemoryChunk> chunks,
            CancellationToken cancellationToken)
        {
            long index = 0;

            await subscription.OnStartAsync(start).ConfigureAwait(false);

            try
            {
                foreach (var chunk in chunks)
                {
                    index = chunk.Index;
                    await _networkSimulator.WaitFast().ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!await subscription.OnNextAsync(Clone(chunk)).ConfigureAwait(false))
                    {
                        await subscription.CompletedAsync(index).ConfigureAwait(false);
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                await subscription.OnErrorAsync(index, e).ConfigureAwait(false);
                return;
            }

            await subscription.CompletedAsync(index).ConfigureAwait(false);
        }

        public Boolean Write(MemoryChunk chunk)
        {
            _lockSlim.EnterWriteLock();
            try
            {
                if (_operations.ContainsKey(chunk.OperationId))
                {
                    return false;
                }

                if (_sortedChunks.ContainsKey(chunk.Index))
                {
                    throw new DuplicateStreamIndexException(this.Id, chunk.Index);
                }

                _operations.Add(chunk.OperationId, chunk);
                _sortedChunks.Add(chunk.Index, chunk);
            }
            finally
            {
                _lockSlim.ExitWriteLock();
            }
            return true;
        }

        public MemoryChunk[] Delete(long fromIndex, long toIndex)
        {
            _lockSlim.EnterReadLock();
            var toDelete = Chunks.Where(x => x.Index >= fromIndex && x.Index <= toIndex).ToArray();
            _lockSlim.ExitReadLock();

            _lockSlim.EnterWriteLock();
            foreach (var chunk in toDelete)
            {
                this._sortedChunks.Remove(chunk.Index);
                this._operations.Remove(chunk.OperationId);
            }
            _lockSlim.ExitWriteLock();

            return toDelete;
        }

        public Task<IChunk> GetByOperationId(string operationId)
        {
            _lockSlim.EnterReadLock();
            _operations.TryGetValue(operationId, out MemoryChunk chunk);
            _lockSlim.ExitReadLock();

            return Task.FromResult((IChunk)Clone(chunk));
        }
    }
}