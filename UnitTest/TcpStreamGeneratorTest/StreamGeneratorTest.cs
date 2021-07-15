/*
 * Copyright 2019 Chris Morgan <chmorgan@gmail.com>
 *
 * GPLv3 licensed, see LICENSE for full text
 * Commercial licensing available
 */
using System;
using System.Reflection;
using System.Collections.Generic;
using SharpPcap;
using SharpPcap.LibPcap;
using PacketDotNet;
using PacketDotNet.Connections;
using log4net;
using Xunit;

namespace Test
{
    public class TcpStreamGeneratorCallbackTest
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private string captureFilename = "../../../captureFiles/tcp_stream_monitor_test.pcap";

        List<TcpStreamGenerator> incomingGenerators;
//        List<TcpStreamGenerator> outgoingGenerators;

        /*
        test process
        1. Read from a packet capture file that contains a tcp stream
        2. Add a tcp connection monitor to the stream identified by the first packet
        3. Process the first packet of the stream which causes a callback
        4. Add another monitor from inside of the callback the first time the callback occurrs
        5. Process another packet which causes two callbacks, one for each set of monitors
        6. Delete 1 of the 2 sets of monitors the second time the callback occurrs
        7. Delete the final monitor the third time the callback occurs(for the same packet as
            in step 6 because we still have two sets of monitors at this point)
        8. Process another packet which should result in the packet not being handled by tcp_packet_stream::handle()
        9. Clean up and exit with success if all events were processed successfully
        */
        [Fact]
        public void TestCallbacks()
        {
            log4net.Config.XmlConfigurator.Configure(LogManager.GetRepository(Assembly.GetCallingAssembly()));

            TcpConnectionManager connectionManager = new TcpConnectionManager();
            connectionManager.OnConnectionFound += HandleConnectionManagerOnConnectionFound;

            incomingGenerators = new List<TcpStreamGenerator>();
//            outgoingGenerators = new List<TcpStreamGenerator>();

            // open the offline file
            var dev = new CaptureFileReaderDevice(captureFilename);
            dev.Open();
            Assert.True(dev != null, "failed to open " + captureFilename);

            PacketCapture e;
            GetPacketStatus status;
            while((status = dev.GetNextPacket(out e)) == GetPacketStatus.PacketRead)
            {
                var rawCapture = e.GetPacket();
                var p = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);
                var tcpPacket = p.Extract<TcpPacket>();
                Console.WriteLine("tcpPacket.PayloadData.Length {0}", tcpPacket.PayloadData.Length);

                connectionManager.ProcessPacket(rawCapture.Timeval, tcpPacket);
            }

            dev.Close();

            int expectedCallbackCount = 5;
            Assert.Equal(expectedCallbackCount, callbackCount);
        }

        void HandleConnectionManagerOnConnectionFound (TcpConnection c)
        {
            Console.WriteLine("HandleConnectionManagerOnConnectionFound c {0}", c.ToString());

            var timeout = new TimeSpan(0, 5, 0);
            TcpStreamGenerator incomingGenerator = new TcpStreamGenerator(c.Flows[0],
                                                                          timeout,
                                                                          100000);
            incomingGenerator.OnCallback += HandleTcpStreamGeneratorOnCallback;
            incomingGenerators.Add(incomingGenerator);

            // We only test the first of the two flows in this unit test
            // so no need to attach to the other flow
#if false
            TcpStreamGenerator outgoingGenerator = new TcpStreamGenerator(c.Flows[1],
                                                                          timeout,
                                                                          100000);
            outgoingGenerator.OnCallback += HandleTcpStreamGeneratorOnCallback;
            outgoingGenerators.Add(outgoingGenerator);
#endif
        }

        private int[] payloadSizes = new int[] {0, 0, 38, 792 };
        private int callbackCount = 0;

        TcpStreamGenerator.CallbackReturnValue HandleTcpStreamGeneratorOnCallback (TcpStreamGenerator tcpStreamGenerator, TcpStreamGenerator.CallbackCondition condition)
        {
            Console.WriteLine("{0} bytes in stream",
                              tcpStreamGenerator.tcpStream.Length);

            int expectedSize = 0;

            for(int i = 0; i < callbackCount; i++)
            {
                expectedSize += payloadSizes[i];
            }

            Assert.Equal(expectedSize, tcpStreamGenerator.tcpStream.Length);

            callbackCount++;
            return TcpStreamGenerator.CallbackReturnValue.ContinueMonitoring;
        }
    }
}
