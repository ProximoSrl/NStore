﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NStore.Core.Processing;

namespace NStore.Domain
{
    public sealed class MessageReaction
    {
        public Object MessageIn { get; private set; }
        public Object[] MessagesOut { get; private set; }

        public MessageReaction(object messageIn, object[] messagesOut)
        {
            MessageIn = messageIn;
            MessagesOut = messagesOut;
        }
    }

    public abstract class ProcessManager<TState> :  
        Aggregate<TState> where TState : class, new()
    {
        protected ProcessManager() : this(null)
        {
        }

        protected ProcessManager(IPayloadProcessor processor) : base(processor)
        {
        }

        public void MessageReceived(object message)
        {
            Emit(message);
        }

        protected override void Track(object @event, object outcome)
        {
            var data = new List<object>();

            if (outcome != null)
            {
                if (outcome is IEnumerable enumerable)
                {
                    data.AddRange(enumerable.Cast<object>());
                }
                else
                {
                    data.Add(outcome);
                }
            }

            var wrapped = new MessageReaction(@event, data.ToArray());
            base.Track(wrapped, outcome);
        }

        protected override IEnumerable<object> PreprocessEvents(object[] events)
        {
            foreach (var o in events)
            {
                if (o is MessageReaction reaction)
                {
                    yield return reaction.MessageIn;
                }

                yield return o;
            }
        }
    }
}