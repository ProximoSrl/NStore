using Newtonsoft.Json;
using NStore.Core.InMemory;
using NStore.Core.Persistence;

// ReSharper disable CheckNamespace
namespace NStore.Persistence.Tests
{
    public partial class BasePersistenceTest
    {
        protected string _mongoConnectionString;
        private const string TestSuitePrefix = "Mongo";

        protected internal IPersistence Create(bool dropOnInit)
        {
            InMemoryPersistenceOptions options = new InMemoryPersistenceOptions(o => DeepClone(o), NoNetworkLatencySimulator.Instance);
            return new InMemoryPersistence(options);
        }

        protected internal void Clear()
        {
            // nothing to do
        }

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings()
        {
            TypeNameHandling = TypeNameHandling.All,
            DateFormatHandling = DateFormatHandling.IsoDateFormat
        };

        private T DeepClone<T>(T source)
        {
            // * * * * * * * * * * * * * * * * * * * * * * *
            //                 DISCLAIMER                 //
            // * * * * * * * * * * * * * * * * * * * * * * *
            //
            // Just a sample, quick & dirty for the tutorial.
            // Don't do it for real
            //
            // * * * * * * * * * * * * * * * * * * * * * * *
            return JsonConvert.DeserializeObject<T>(
                JsonConvert.SerializeObject(source, Settings),
                Settings
            );
        }
    }
}