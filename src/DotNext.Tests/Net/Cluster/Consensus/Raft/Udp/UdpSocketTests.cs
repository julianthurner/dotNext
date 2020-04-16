using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace DotNext.Net.Cluster.Consensus.Raft.Udp
{
    using TransportServices;

    [ExcludeFromCodeCoverage]
    public sealed class UdpSocketTests : TransportTestSuite
    {
        private readonly Func<long> appIdGenerator = new Random().Next<long>;

        [Fact]
        public Task RequestResponse()
        {
            static UdpServer CreateServer(ILocalMember member, IPEndPoint address, TimeSpan timeout) => new UdpServer(address, 2, ArrayPool<byte>.Shared, ExchangePoolFactory(member), NullLoggerFactory.Instance)
            {
                ReceiveTimeout = timeout,
                DatagramSize = UdpSocket.MaxDatagramSize,
                DontFragment = false
            };
            UdpClient CreateClient(IPEndPoint address) => new UdpClient(address, 2, ArrayPool<byte>.Shared, appIdGenerator, NullLoggerFactory.Instance)
            {
                DatagramSize = UdpSocket.MaxDatagramSize,
                DontFragment = false
            };
            return RequestResponseTest(CreateServer, CreateClient);
        }

        [Fact]
        public Task StressTest()
        {
            static UdpServer CreateServer(ILocalMember member, IPEndPoint address, TimeSpan timeout) => new UdpServer(address, 100, ArrayPool<byte>.Shared, ExchangePoolFactory(member), NullLoggerFactory.Instance)
            {
                ReceiveTimeout = timeout,
                DatagramSize = UdpSocket.MaxDatagramSize,
                DontFragment = false
            };
            UdpClient CreateClient(IPEndPoint address) => new UdpClient(address, 100, ArrayPool<byte>.Shared, appIdGenerator, NullLoggerFactory.Instance)
            {
                DatagramSize = UdpSocket.MaxDatagramSize,
                DontFragment = false
            };
            return StressTestTest(CreateServer, CreateClient);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task MetadataRequestResponse(bool smallAmountOfMetadata)
        {
            static UdpServer CreateServer(ILocalMember member, IPEndPoint address, TimeSpan timeout) => new UdpServer(address, 100, ArrayPool<byte>.Shared, ExchangePoolFactory(member), NullLoggerFactory.Instance)
            {
                ReceiveTimeout = timeout,
                DatagramSize = UdpSocket.MinDatagramSize,
                DontFragment = true
            };
            UdpClient CreateClient(IPEndPoint address) => new UdpClient(address, 100, ArrayPool<byte>.Shared, appIdGenerator, NullLoggerFactory.Instance)
            {
                DatagramSize = UdpSocket.MinDatagramSize,
                DontFragment = true
            };
            return MetadataRequestResponseTest(CreateServer, CreateClient, smallAmountOfMetadata);
        }

        [Theory]
        [InlineData(512, ReceiveEntriesBehavior.ReceiveAll)]
        [InlineData(512, ReceiveEntriesBehavior.ReceiveFirst)]
        [InlineData(512, ReceiveEntriesBehavior.DropAll)]
        [InlineData(512, ReceiveEntriesBehavior.DropFirst)]
        [InlineData(50, ReceiveEntriesBehavior.ReceiveAll)]
        [InlineData(50, ReceiveEntriesBehavior.ReceiveFirst)]
        [InlineData(50, ReceiveEntriesBehavior.DropAll)]
        [InlineData(50, ReceiveEntriesBehavior.DropFirst)]
        public Task SendingLogEntries(int payloadSize, ReceiveEntriesBehavior behavior)
        {
            static UdpServer CreateServer(ILocalMember member, IPEndPoint address, TimeSpan timeout) => new UdpServer(address, 100, ArrayPool<byte>.Shared, ExchangePoolFactory(member), NullLoggerFactory.Instance)
            {
                DatagramSize = UdpSocket.MinDatagramSize,
                ReceiveTimeout = timeout,
                DontFragment = true
            };
            UdpClient CreateClient(IPEndPoint address) => new UdpClient(address, 100, ArrayPool<byte>.Shared, appIdGenerator, NullLoggerFactory.Instance)
            {
                DatagramSize = UdpSocket.MinDatagramSize,
                DontFragment = true
            };
            return SendingLogEntriesTest(CreateServer, CreateClient, payloadSize, behavior);
        }

        [Theory]
        [InlineData(512)]
        [InlineData(50)]
        public Task SendingSnapshot(int payloadSize)
        {
            static UdpServer CreateServer(ILocalMember member, IPEndPoint address, TimeSpan timeout) => new UdpServer(address, 100, ArrayPool<byte>.Shared, ExchangePoolFactory(member), NullLoggerFactory.Instance)
            {
                DatagramSize = UdpSocket.MinDatagramSize,
                ReceiveTimeout = timeout,
                DontFragment = true
            };
            UdpClient CreateClient(IPEndPoint address) => new UdpClient(address, 100, ArrayPool<byte>.Shared, appIdGenerator, NullLoggerFactory.Instance)
            {
                DatagramSize = UdpSocket.MinDatagramSize,
                DontFragment = true
            };
            return SendingSnapshotTest(CreateServer, CreateClient, payloadSize);
        }
    }
}