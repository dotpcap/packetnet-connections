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
using PacketDotNet.Connections.Tools;
using Xunit;

namespace Test
{
    public class ConnectionStatisticsTest
    {
        private string captureFN = "tcp_test.pcap";
        private string capturePre = "../../../captureFiles/";
        private string captureFilename;

        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public PacketDotNet.Connections.Tools.ConnectionStatistics connectionStatistics = new PacketDotNet.Connections.Tools.ConnectionStatistics();

        [Fact]
        public void Test()
        {
            connectionStatistics.OnMeasurementFound += HandleConnectionStatisticsOnMeasurementFound;
            connectionStatistics.OnMeasurementEvent += HandleConnectionStatisticsOnMeasurementEvent;
            captureFilename = capturePre + captureFN;

            // open the offline file
            var dev = new CaptureFileReaderDevice(captureFilename);
            dev.Open();
            Assert.True(dev != null, "failed to open " + captureFilename);

            var tcpConnectionManager = new TcpConnectionManager();
            tcpConnectionManager.OnConnectionFound += HandleTcpConnectionManagerOnConnectionFound;

            RawCapture rawCapture;
            while((rawCapture = dev.GetNextPacket()) != null)
            {
                var p = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);
                var tcpPacket = p.Extract<TcpPacket>();

                if(tcpPacket != null)
                {
//                    Console.WriteLine("{0}", tcpPacket.ToString());

                    tcpConnectionManager.ProcessPacket(rawCapture.Timeval, tcpPacket);
                }
            }
        }

        void HandleConnectionStatisticsOnMeasurementEvent (ConnectionStatistics connectionStatistics, TcpConnection c, ConnectionStatistics.EventTypes eventType)
        {
            Console.WriteLine("eventType: {0}:{1} <-> {2}:{3} {4}",
                              c.Flows[0].address.ToString(),
                              c.Flows[0].port,
                              c.Flows[1].address,
                              c.Flows[1].port,
                              eventType);
        }

        void HandleConnectionStatisticsOnMeasurementFound (ConnectionStatistics connectionStatistics, TcpConnection c, ConnectionStatistics.ConnectionTimes connectionTimes)
        {
            Console.WriteLine("measurement: {0}:{1} <-> {2}:{3} - {4}",
                              c.Flows[0].address.ToString(),
                              c.Flows[0].port,
                              c.Flows[1].address,
                              c.Flows[1].port,
                              connectionTimes);
        }

        void HandleTcpConnectionManagerOnConnectionFound (TcpConnection c)
        {
            Console.WriteLine("Connection found: {0}:{1} <-> {2}:{3}",
                              c.Flows[0].address.ToString(),
                              c.Flows[0].port,
                              c.Flows[1].address,
                              c.Flows[1].port);

            connectionStatistics.MonitorConnection(c);
        }
    }
}

