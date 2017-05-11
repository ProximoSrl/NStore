﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NStore.Raw;
using NStore.Streams;

namespace NStore.Aggregates
{
    public class Repository : IRepository
    {
        private readonly IAggregateFactory _factory;
        private readonly IStreamStore _streams;
        private readonly IDictionary<IAggregate, IStream> _openedStreams = new Dictionary<IAggregate, IStream>();
        private readonly IDictionary<string, IAggregate> _identityMap = new Dictionary<string, IAggregate>();

        public Repository(IAggregateFactory factory, IStreamStore streams)
        {
            _factory = factory;
            _streams = streams;
        }

        public async Task<T> GetById<T>(
            string id,
            int version = Int32.MaxValue,
            CancellationToken cancellationToken = default(CancellationToken)
        ) where T : IAggregate
        {
            if (_identityMap.ContainsKey(id))
            {
                return (T) _identityMap[id];
            }

            var aggregate = _factory.Create<T>();
            _identityMap.Add(id, aggregate);

            aggregate.Init(id);
            var stream = OpenStream(aggregate);
            var persister = (IAggregatePersister) aggregate;

            var consumer = new LambdaConsumer((l, payload) =>
            {
                var commit = (Commit) payload;

                persister.AppendCommit(commit);
                return ScanCallbackResult.Continue;
            });

            await stream.Read(consumer, 0, version, cancellationToken)
                .ConfigureAwait(false);

            return aggregate;
        }

        public async Task Save<T>(
            T aggregate,
            string operationId,
            Action<IHeadersAccessor> headers = null,
            CancellationToken cancellationToken = default(CancellationToken)
        ) where T : IAggregate
        {
            var stream = GetStream(aggregate);
            var persister = (IAggregatePersister) aggregate;

            var commit = persister.BuildCommit();

            headers?.Invoke(commit);

            await stream.Append(commit, operationId, cancellationToken).ConfigureAwait(false);
        }

        private IStream OpenStream(IAggregate aggregate)
        {
            var s = _streams.OpenOptimisticConcurrency(aggregate.Id);
            _openedStreams.Add(aggregate, s);
            return s;
        }

        private IStream GetStream(IAggregate aggregate)
        {
            try
            {
                return _openedStreams[aggregate];
            }
            catch (KeyNotFoundException e)
            {
                throw new RepositoryMismatchException();
            }
        }
    }
}