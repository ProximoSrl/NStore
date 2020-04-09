using MongoDB.Driver;
using NStore.Core.Logging;
using System;

namespace NStore.Persistence.Mongo
{
    public class MongoPersistenceOptions
    {
        public INStoreLoggerFactory LoggerFactory { get; set; }

        public string PartitionsConnectionString { get; set; }
        public string PartitionsCollectionName { get; set; } = "chunks";

        /// <summary>
        /// ConnectionString that points to database to use to generate sequences
        /// if we want to use sequences generated in database.
        /// </summary>
        public string SequenceConnectionString { get; set; }

        /// <summary>
        /// Collection name to use to store generated sequences.
        /// </summary>
        public string SequenceCollectionName { get; set; } = "seq";
        public string SequenceId { get; set; } = "streams";
        public bool UseLocalSequence { get; set; } = false;
        public bool DropOnInit { get; set; } = false;

        public IMongoPayloadSerializer MongoPayloadSerializer { get; set; }

        /// <summary>
        /// This allows customization of the connection string for Partition Data
        /// </summary>
        public Action<MongoUrlBuilder> CustomizePartitionSettings { get; set; }

        /// <summary>
        /// This allows the customization of MongoClientSettings used to create mongo client
        /// for the Partition Database
        /// </summary>
        public Action<MongoClientSettings> CustomizePartitionClientSettings { get; set; }

        /// <summary>
        /// This allows customization of the connection string for Sequence Data.
        /// </summary>
        public Action<MongoUrlBuilder> CustomizeSequenceSettings { get; set; }

        /// <summary>
        /// This allows the customization of MongoClientSettings used to create mongo client
        /// for the Sequence Database
        /// </summary>
        public Action<MongoClientSettings> CustomizeSequenceClientSettings { get; set; }

        public bool IsValid()
        {
            return !String.IsNullOrWhiteSpace(PartitionsConnectionString);
        }

        public MongoPersistenceOptions()
        {
#pragma warning disable RCS1163 // Unused parameter.
            this.CustomizePartitionSettings = settings => { };
            this.CustomizeSequenceSettings = settings => { };

            this.CustomizePartitionClientSettings = clientSettings => { };
            this.CustomizeSequenceClientSettings = clientSettings => { };
#pragma warning restore RCS1163 // Unused parameter.
            LoggerFactory = NStoreNullLoggerFactory.Instance;
        }
    }
}