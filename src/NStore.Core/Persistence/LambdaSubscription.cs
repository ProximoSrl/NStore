using System;
using System.Threading.Tasks;

namespace NStore.Core.Persistence
{
    public class LambdaSubscription : ISubscription
    {
        public delegate Task OnErrorDelegate(long position, Exception ex);
        public delegate Task OnStartDelegate(long position);
        public delegate Task OnCompleteDelegate(long position);
        public delegate Task OnStopDelegate(long position);

        private readonly ChunkProcessor _fn;
        private Exception _failed;
        private long _failedPosition;

        public bool ReadCompleted { get; private set; }
        public bool Failed => this._failed != null;
        public long FailedPosition => _failedPosition;
        public Exception LastError => _failed;

        public OnErrorDelegate OnError { get; set; }
        public OnStartDelegate OnStart { get; set; }
        public OnCompleteDelegate OnComplete { get; set; }
        public OnStopDelegate OnStop { get; set; }

        public LambdaSubscription(ChunkProcessor fn)
        {
            _fn = fn;
        }

        public Task<bool> OnNextAsync(IChunk chunk)
        {
            return this._fn(chunk);
        }

        public Task OnStartAsync(long indexOrPosition)
        {
            return OnStart != null
                ? OnStart(indexOrPosition)
                : Task.CompletedTask;
        }

        public Task CompletedAsync(long indexOrPosition)
        {
            this.ReadCompleted = true;

            return OnComplete != null
                ? OnComplete(indexOrPosition)
                : Task.CompletedTask;
        }

        public Task StoppedAsync(long indexOrPosition)
        {
            this.ReadCompleted = true;
            return OnStop != null
                ? OnStop(indexOrPosition)
                : Task.CompletedTask;
        }

        public Task OnErrorAsync(long indexOrPosition, Exception ex)
        {
            _failed = ex;
            _failedPosition = indexOrPosition;

            return OnError != null
                ? OnError(indexOrPosition, ex)
                : Task.CompletedTask;
        }
    }
}