using NStore.Core.InMemory;
using NStore.Core.Persistence;
using NStore.Core.Processing;
using NStore.Core.Snapshots;
using NStore.Core.Streams;
using NStore.Domain.Experimental;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace NStore.Domain.Tests.ExperimentalTests
{
    public class TweetId
    {
        private readonly string _id;

        public TweetId(string id)
        {
            _id = id;
        }

        public TweetId() : this(Guid.NewGuid().ToString())
        {
        }

        public static implicit operator string(TweetId id) => id._id;
    }

    public class TweetPosted
    {
        public TweetPosted(TweetId tweetId, string userId, string message)
        {
            TweetId = tweetId;
            UserId = userId;
            Message = message;
        }

        public TweetId TweetId { get; private set; }
        public string UserId { get; private set; }
        public string Message { get; private set; }
    }

    public class TweetFavorited
    {
        public TweetFavorited(TweetId tweetId, string userId)
        {
            TweetId = tweetId;
            UserId = userId;

            When = DateTime.UtcNow;
        }

        public TweetId TweetId { get; private set; }
        public string UserId { get; private set; }
        public DateTime When { get; private set; }
    }

    public class Tweet
    {
        public TweetId Id { get; private set; }
        public string Text { get; private set; }
        public long Favs { get; private set; }

        public Tweet(TweetId id, string text)
        {
            Id = id;
            Text = text;
        }

        public void AddFav() => Favs++;
    }

    public class TimeLine : IEnumerable<TweetId>
    {
        private readonly Queue<TweetId> _tweets = new Queue<TweetId>();
        private const int MaxSize = 20;

        public void Add(TweetId tweetId)
        {
            _tweets.Enqueue(tweetId);

            if (_tweets.Count > MaxSize)
            {
                _tweets.Dequeue();
            }
        }

        public IEnumerator<TweetId> GetEnumerator()
        {
            return _tweets.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public interface ITwitterQueryModel
    {
        Tweet TweetById(TweetId tweetId);
        Task<IEnumerable<Tuple<string, DateTime>>> FavsOfTweetAsync(TweetId tweetId);
        IEnumerable<Tweet> TimelineOf(string userId);
    }

    public class TwitterViews : ITwitterQueryModel
    {
        private readonly IDictionary<TweetId, Tweet> _tweets = new Dictionary<TweetId, Tweet>();
        private readonly IDictionary<string, TimeLine> _timelines = new Dictionary<string, TimeLine>();
        private readonly Func<string, IReadOnlyStream> _streamFactory;

        public TwitterViews(Func<string, IReadOnlyStream> streamFactory)
        {
            _streamFactory = streamFactory;
        }

        public Task<bool> Watch(IChunk chunk)
        {
            this.CallNonPublicIfExists("On", chunk.Payload);
            return Task.FromResult(true);
        }

        private TimeLine GetTimeline(string userId)
        {
            if (!_timelines.TryGetValue(userId, out var tl))
            {
                tl = new TimeLine();
                _timelines.Add(userId, tl);
            }

            return tl;
        }

        private void On(TweetPosted posted)
        {
            var tweet = new Tweet(posted.TweetId, posted.Message);
            _tweets.Add(posted.TweetId, tweet);
            GetTimeline(posted.UserId).Add(tweet.Id);
        }

        private void On(TweetFavorited favorited)
        {
            TweetById(favorited.TweetId).AddFav();
        }

        public Tweet TweetById(TweetId tweetId)
        {
            return _tweets[tweetId];
        }

        public async Task<IEnumerable<Tuple<string, DateTime>>> FavsOfTweetAsync(TweetId tweetId)
        {
            var recorded = await _streamFactory($"{tweetId}/favs").RecordAsync();
            return recorded.Data
                .Cast<TweetFavorited>()
                .Select(f => new Tuple<string, DateTime>(f.UserId, f.When));
        }

        public IEnumerable<Tweet> TimelineOf(string userId)
        {
            return GetTimeline(userId).Select(TweetById);
        }
    }

    public class TwitterEngine
    {
        private readonly IPersistence _persistence = new InMemoryPersistence(new InMemoryPersistenceOptions());
        private readonly DomainRuntime _runtime;
        private readonly TwitterViews _views;
        public ITwitterQueryModel Query => _views;

        public TwitterEngine()
        {
            _views = new TwitterViews(id => new ReadOnlyStream(id, _persistence));
            _runtime = new DomainBuilder()
                .PersistOn(() => _persistence)
                .WithSnapshotsOn(() => new DefaultSnapshotStore(new InMemoryPersistence(new InMemoryPersistenceOptions())))
                .CreateAggregatesWith(() => new DefaultAggregateFactory())
                .BroadcastTo(_views.Watch)
                .Build();
        }

        public Task StopAsync()
        {
            return _runtime.ShutdownAsync();
        }

        public async Task<TweetId> PostAsync(string user, string message)
        {
            var tweet = new TweetPosted(new TweetId(), user, message);
            await _runtime.PushAsync($"{user}/tweets", tweet);
            return tweet.TweetId;
        }

        public Task FavoriteAsync(TweetId tweetId, string favoritedByUserId)
        {
            return _runtime.PushAsync($"{tweetId}/favs", new TweetFavorited(tweetId, favoritedByUserId));
        }

        public Task CatchUpAsync()
        {
            return _runtime.CatchUpAsync();
        }
    }

    public class TwitterDomainTests
    {
        [Fact]
        public async Task twitter_lite()
        {
            var twitter = new TwitterEngine();

            var tweetId = await twitter.PostAsync("user_1", "hello twitter!");
            await twitter.FavoriteAsync(tweetId, "user_24");
            await twitter.FavoriteAsync(tweetId, "user_26");

            await twitter.CatchUpAsync();

            var favCount = twitter.Query.TweetById(tweetId).Favs;
            var favs = await twitter.Query.FavsOfTweetAsync(tweetId);
            var tl = twitter.Query.TimelineOf("user_1");

            Assert.Equal(2, favCount);
            Assert.Collection(favs,
                fav1 => Assert.Equal("user_24", fav1.Item1),
                fav2 => Assert.Equal("user_26", fav2.Item1)
            );

            Assert.Collection(tl,
                tweet => Assert.Equal("hello twitter!", tweet.Text)
            );
        }
    }
}