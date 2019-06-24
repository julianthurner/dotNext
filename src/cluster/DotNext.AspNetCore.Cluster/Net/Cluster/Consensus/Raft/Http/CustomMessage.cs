﻿using Microsoft.AspNetCore.Http;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using static System.Globalization.CultureInfo;

namespace DotNext.Net.Cluster.Consensus.Raft.Http
{
    using Messaging;
    using NullMessage = Threading.Tasks.CompletedTask<Messaging.IMessage, Generic.DefaultConst<Messaging.IMessage>>;
    using static Threading.Tasks.Conversion;

    internal class CustomMessage : HttpMessage, IHttpMessageWriter<IMessage>, IHttpMessageReader<IMessage>
    {
        internal new const string MessageType = "CustomMessage";
        private const string OneWayHeader = "X-OneWay-Message";

        private const string RespectLeadershipHeader = "X-Respect-Leadership";

        internal readonly bool IsOneWay;
        internal readonly IMessage Message;
        internal bool RespectLeadership;

        internal CustomMessage(IPEndPoint sender, IMessage message, bool oneWay)
            : base(MessageType, sender)
        {
            Message = message;
            IsOneWay = oneWay;
        }

        internal CustomMessage(HttpRequest request)
            : base(request)
        {
            foreach (var header in request.Headers[OneWayHeader])
                if (bool.TryParse(header, out IsOneWay))
                    break;
            foreach (var header in request.Headers[RespectLeadershipHeader])
                if(bool.TryParse(header, out RespectLeadership))
                    break;
            Message = new InboundMessageContent(request);
        }

        private protected sealed override void FillRequest(HttpRequestMessage request)
        {
            base.FillRequest(request);
            request.Headers.Add(OneWayHeader, Convert.ToString(IsOneWay, InvariantCulture));
            request.Headers.Add(RespectLeadershipHeader, Convert.ToString(RespectLeadershipHeader, InvariantCulture));
            request.Content = new OutboundMessageContent(Message);
        }

        public Task SaveResponse(HttpResponse response, IMessage message)
        {
            response.StatusCode = StatusCodes.Status200OK;
            return OutboundMessageContent.WriteTo(message, response);
        }

        Task<IMessage> IHttpMessageReader<IMessage>.ParseResponse(HttpResponseMessage response)
            => response.StatusCode == HttpStatusCode.NoContent ? NullMessage.Task : InboundMessageContent.FromResponseAsync(response).Convert<InboundMessageContent, IMessage>();
    }

    internal sealed class CustomMessage<T> : CustomMessage, IHttpMessageReader<T>
    {
        private readonly MessageReader<T> reader;

        internal CustomMessage(IPEndPoint sender, IMessage message, MessageReader<T> reader) : base(sender, message, false) => this.reader = reader;

        async Task<T> IHttpMessageReader<T>.ParseResponse(HttpResponseMessage response)
        {
            using (var message = await InboundMessageContent.FromResponseAsync(response).ConfigureAwait(false))
                return await reader(message).ConfigureAwait(false);
        }
    }
}
