using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using SharpPcap;
using PacketDotNet;
using PacketDotNet.Connections;
using log4net;

namespace BandwidthMonitor
{
    /// <summary>
    /// Example application showing how to calculate bandwidth consumed by udp and tcp
    /// connections, by port
    /// </summary>
    class MainClass
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static TcpConnectionManager tcpConnectionManager = new TcpConnectionManager();

        public static void Main (string[] args)
        {
            // Print SharpPcap version
            var ver = SharpPcap.Pcap.SharpPcapVersion;
            Console.WriteLine("SharpPcap {0}", ver);

            // Retrieve the device list
            var devices = SharpPcap.LibPcap.LibPcapLiveDeviceList.Instance;

            // If no devices were found print an error
            if(devices.Count < 1)
            {
                Console.WriteLine("No devices were found on this machine");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("The following devices are available on this machine:");
            Console.WriteLine("----------------------------------------------------");
            Console.WriteLine();

            int i = 0;

            // Print out the devices
            foreach(SharpPcap.LibPcap.LibPcapLiveDevice dev in devices)
            {
                /* Description */
                Console.WriteLine("{0}) {1} {2}", i, dev.Name, dev.Description);
                i++;
            }

            Console.WriteLine();
            Console.Write("-- Please choose a device to capture: ");
            i = int.Parse( Console.ReadLine() );

            ICaptureDevice device = devices[i];

            tcpConnectionManager.OnConnectionFound += HandleTcpConnectionManagerOnConnectionFound;

            // Register our handler function to the 'packet arrival' event
            device.OnPacketArrival +=
                new PacketArrivalEventHandler( device_OnPacketArrival );

            // Open the device for capturing
            int readTimeoutMilliseconds = 1000;
            device.Open(DeviceModes.Promiscuous, readTimeoutMilliseconds);

            Console.WriteLine();
            Console.WriteLine("-- Listening on {0} {1}, hit 'Enter' to stop...",
                device.Name, device.Description);

            // Start the capturing process
            device.StartCapture();

            // start up a thread that will periodically lock and prune the collection
            var pruningThread = new Thread(DictionaryPruner);
            pruningThread.Start();

            // start up a thread that will periodically calculate and output bandwidth
            var calculationAndDisplayThread = new Thread(CalculateAndDisplay);
            calculationAndDisplayThread.Start();

            // Wait for 'Enter' from the user.
            Console.ReadLine();

            // Stop the capturing process
            device.StopCapture();

            // stop the pruning thread and wait for it to stop
            DictionaryPrunerStop = true;
            pruningThread.Join();

            CalculateAndDisplayStop = true;
            calculationAndDisplayThread.Join();

            Console.WriteLine("-- Capture stopped.");

            Console.WriteLine ("Captured {0} packets", PacketCount);
        }

        /// <summary>
        /// How old can a packet be before it is expired
        /// </summary>
        private static TimeSpan expirationAge = new TimeSpan(0, 0, 20);

        private static bool DictionaryPrunerStop = false;

        private static void DictionaryPruner()
        {
            int removedCount;
            while(!DictionaryPrunerStop)
            {
                removedCount = 0;

                lock(DictionaryLock)
                {
                    removedCount += PruneTcpFlowDictionary(TcpPackets);

                    if(removedCount != 0)
                    {
                        Console.WriteLine("Pruned {0} packets", removedCount);
                    }
                }

                Thread.Sleep (1000);
            }
        }

        private static int PruneTcpFlowDictionary(Dictionary<TcpFlow, List<Entry>> dictionary)
        {
            var removedCount = 0;
            var flowsToRemove = new List<TcpFlow>();

            foreach(var kvp in dictionary)
            {
                removedCount += kvp.Value.RemoveAll (x => ((DateTime.UtcNow - x.Timeval.Date) > expirationAge));

                // if the key is closed and there are no remaining packets
                // we can remove this TcpFlow
                if(!kvp.Key.IsOpen && (kvp.Value.Count == 0))
                {
                    flowsToRemove.Add (kvp.Key);
                }
            }

            // remove the flows outside of the foreach() to avoid sync issues
            foreach(var flow in flowsToRemove)
            {
                dictionary.Remove(flow);
            }

            return removedCount;
        }

        private static int PruneDictionary(Dictionary<long, List<Entry>> dictionary)
        {
            var removedCount = 0;

            foreach(var kvp in dictionary)
            {
                removedCount += kvp.Value.RemoveAll (x => ((DateTime.UtcNow - x.Timeval.Date) > expirationAge));
            }

            return removedCount;
        }

        private static List<TcpConnection> OpenConnections = new List<TcpConnection>();

        private static object DictionaryLock = new object();
        private static Dictionary<TcpFlow, List<Entry>> TcpPackets = new Dictionary<TcpFlow, List<Entry>>();

        static void HandleTcpConnectionManagerOnConnectionFound (TcpConnection c)
        {
            OpenConnections.Add (c);
            c.OnConnectionClosed += HandleCOnConnectionClosed;
            c.Flows[0].OnPacketReceived += HandleOnPacketReceived;
            c.Flows[0].OnFlowClosed += HandleOnFlowClosed;
            c.Flows[1].OnPacketReceived += HandleOnPacketReceived;
            c.Flows[1].OnFlowClosed += HandleOnFlowClosed;
        }

        static void HandleCOnConnectionClosed (PosixTimeval timeval, TcpConnection connection, PacketDotNet.TcpPacket tcp, TcpConnection.CloseType closeType)
        {
            connection.OnConnectionClosed -= HandleCOnConnectionClosed;
            OpenConnections.Remove(connection);
        }

        static void HandleOnFlowClosed (PosixTimeval timeval, PacketDotNet.TcpPacket tcp, TcpConnection connection, TcpFlow flow)
        {
            // unhook the received handler
            flow.OnPacketReceived -= HandleOnPacketReceived;
        }

        static void HandleOnPacketReceived (PosixTimeval timeval, PacketDotNet.TcpPacket tcp, TcpConnection connection, TcpFlow flow)
        {
            lock(DictionaryLock)
            {
                if(!TcpPackets.ContainsKey(flow))
                {
                    TcpPackets[flow] = new List<Entry>();
                }

                var entry = new Entry(timeval, tcp);
                TcpPackets[flow].Add (entry);
            }
        }

        public class Entry
        {
            public SharpPcap.PosixTimeval Timeval;
            public PacketDotNet.Packet Packet;

            public Entry(SharpPcap.PosixTimeval timeval, PacketDotNet.Packet packet)
            {
                this.Timeval = timeval;
                this.Packet = packet;
            }
        }

        private static int PacketCount = 0;

        /// <summary>
        /// Prints the time and length of each received packet
        /// </summary>
        private static void device_OnPacketArrival(object sender, PacketCapture e)
        {
            PacketCount++;

            var rawPacket = e.GetPacket();
            var p = PacketDotNet.Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);

            var tcpPacket = p.Extract<TcpPacket>();

            if(tcpPacket != null)
            {
                log.Debug("passing packet to TcpConnectionManager");
                tcpConnectionManager.ProcessPacket(rawPacket.Timeval,
                                                   tcpPacket);
            }
        }

        private static bool CalculateAndDisplayStop = false;

        private static void CalculateAndDisplay()
        {
            var bandwidthInterval = new TimeSpan(0, 0, 5);

            while(!CalculateAndDisplayStop)
            {
                // calculate the bandwidth for all ports over the last second
                lock(DictionaryLock)
                {
                    Console.WriteLine ("Open connections:");
                    foreach(var tcpConnection in OpenConnections)
                    {
                        // calculate the flow bandwidths
                        var flow0Bandwidth = CalculateBandwidth(tcpConnection.Flows[0],
                                                                bandwidthInterval);
                        var flow1Bandwidth = CalculateBandwidth(tcpConnection.Flows[1],
                                                                bandwidthInterval);

                        Console.WriteLine ("{0}:{1} {2} bytes out <-> {3}:{4} {5} bytes out",
                                           tcpConnection.Flows[0].address,
                                           tcpConnection.Flows[0].port,
                                           flow0Bandwidth,
                                           tcpConnection.Flows[1].address,
                                           tcpConnection.Flows[1].port,
                                           flow1Bandwidth);
                    }
                }

                Thread.Sleep (bandwidthInterval);
            }
        }

        private static int CalculateBandwidth(TcpFlow flow, TimeSpan interval)
        {
            int bandwidth = 0;

            if(!TcpPackets.ContainsKey(flow))
                return bandwidth;

            var now = DateTime.UtcNow;
            var entryArray = TcpPackets[flow];
            bandwidth = (from entry in entryArray
                         where ((now - entry.Timeval.Date) < interval)
                         select entry.Packet.Bytes.Length).Sum ();
            return bandwidth;
        }
    }
}
