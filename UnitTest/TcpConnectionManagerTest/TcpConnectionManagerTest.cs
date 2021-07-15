/*
 * Copyright 2019 Chris Morgan <chmorgan@gmail.com>
 *
 * GPLv3 licensed, see LICENSE for full text
 * Commercial licensing available
 */
using System;
using SharpPcap;
using SharpPcap.LibPcap;
using PacketDotNet;
using PacketDotNet.Connections;
using Xunit;

namespace Test
{
    public class TcpConnectionManagerTest
    {
        private string capturePre = "../../../captureFiles/";

        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private int connectionsFound;

        [Fact]
        public void ConnectionCreation()
        {
            // store the logging value
            var oldThreshold = LoggingConfiguration.GlobalLoggingLevel;

            // disable logging to improve performance
            LoggingConfiguration.GlobalLoggingLevel = log4net.Core.Level.Off;

            string captureFN = "tcp_test.pcap";
            string captureFilename = capturePre + captureFN;

            // open the offline file
            var dev = new CaptureFileReaderDevice(captureFilename);
            dev.Open();
            Assert.True(dev != null, "failed to open " + captureFilename);

            var tcpConnectionManager = new TcpConnectionManager();
            tcpConnectionManager.OnConnectionFound += HandleTcpConnectionManagerOnConnectionFound;

            connectionsFound = 0;
            var startTime = DateTime.Now;
            PacketCapture e;
            GetPacketStatus status;
            while((status = dev.GetNextPacket(out e)) == GetPacketStatus.PacketRead)
            {
                var rawCapture = e.GetPacket();
                var p = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);
                var tcpPacket = p.Extract<TcpPacket>();

                if(tcpPacket != null)
                {
//                    Console.WriteLine("{0}", tcpPacket.ToString());

                    tcpConnectionManager.ProcessPacket(rawCapture.Timeval, tcpPacket);
                }
            }
            var endTime = DateTime.Now;

            // restore logging
            LoggingConfiguration.GlobalLoggingLevel = oldThreshold;

            var rate = new Utils.Rate(startTime, endTime, connectionsFound, "Connections found");
            Console.WriteLine(rate.ToString());
        }

        /// <summary>
        /// For ConnectionCreation test
        /// </summary>
        /// <param name="c">
        /// A <see cref="TcpConnection"/>
        /// </param>
        void HandleTcpConnectionManagerOnConnectionFound (TcpConnection c)
        {
            connectionsFound++;

            log.DebugFormat("Connection found: {0}:{1} <-> {2}:{3}",
                              c.Flows[0].address.ToString(),
                              c.Flows[0].port,
                              c.Flows[1].address,
                              c.Flows[1].port);
        }

        /// <summary>
        /// Test that a connection is found and properly closed and that
        /// reset packets that follow are ignored and don't result in
        /// another connection being created
        /// </summary>
        [Fact]
        public void ConnectionEstablishResetIgnored()
        {
            string captureFN = "tcp_stream_with_disconnect_and_reset.pcap";
            string captureFilename = capturePre + captureFN;

            // open the offline file
            var dev = new CaptureFileReaderDevice(captureFilename);
            dev.Open();
            Assert.True(dev != null, "failed to open " + captureFilename);

            int expectedConnectionsFound = 1;
            int connectionsFound = 0;
            var tcpConnectionManager = new TcpConnectionManager();
            tcpConnectionManager.OnConnectionFound += delegate(TcpConnection c) {
                Console.WriteLine("found connection");
                connectionsFound++;
            };

            tcpConnectionManager.OnConnectionFound += HandleTcpConnectionManagerOnConnectionFound;

            GetPacketStatus status;
            PacketCapture e;
            while((status = dev.GetNextPacket(out e)) == GetPacketStatus.PacketRead)
            {
                var rawCapture = e.GetPacket();
                var p = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);
                var tcpPacket = p.Extract<TcpPacket>();

                if(tcpPacket != null)
                {
//                    Console.WriteLine("{0}", tcpPacket.ToString());

                    tcpConnectionManager.ProcessPacket(rawCapture.Timeval, tcpPacket);
                }
            }

            Assert.Equal(expectedConnectionsFound, connectionsFound);
        }
    }
}
