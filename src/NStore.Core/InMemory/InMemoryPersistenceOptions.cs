using System;

namespace NStore.Core.InMemory
{
    public class InMemoryPersistenceOptions
    {
        public InMemoryPersistenceOptions() : this(null, null)
        {

        }

        public InMemoryPersistenceOptions(Func<object, object> cloneFunc, INetworkSimulator networkSimulator)
        {
            CloneFunc = cloneFunc;
            NetworkSimulator = networkSimulator;
        }

        public Func<object, object> CloneFunc { get; set; }

        public INetworkSimulator NetworkSimulator { get; set; }
    }
}
