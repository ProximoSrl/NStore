using System.Threading.Tasks;

namespace NStore.Core.InMemory
{
    public class NoNetworkLatencySimulator : INetworkSimulator
    {
        public static INetworkSimulator Instance { get; private set; } = new NoNetworkLatencySimulator();

        public Task<long> WaitFast()
        {
            return Task.FromResult(0L);
        }

        public Task<long> Wait()
        {
            return Task.FromResult(0L);
        }
    }
}