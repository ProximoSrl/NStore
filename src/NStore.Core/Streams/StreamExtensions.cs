﻿using System.Threading;
using System.Threading.Tasks;

namespace NStore.Core.Streams
{
    public static class StreamExtensions
    {
        public static Task AppendAsync(this IStream stream, object payload)
        {
            return stream.AppendAsync(payload, null, CancellationToken.None);
        }

        public static Task AppendAsync(this IStream stream, object payload, string operationId)
        {
            return stream.AppendAsync(payload, operationId, CancellationToken.None);
        }

        public static Task DeleteAsync(this IStream stream)
        {
            return stream.DeleteAsync(CancellationToken.None);
        }
    }
}