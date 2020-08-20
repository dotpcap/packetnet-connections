using System;
using SharpPcap;
using SharpPcap.LibPcap;
using SharpPcap.WinPcap;
using PacketDotNet;
using PacketDotNet.Connections;
using PacketDotNet.Utils;

namespace ConnectionsExample
{
    class MainClass
    {
        private static TcpConnectionManager tcpConnectionManager;

        public static void Main (string[] args)
        {
            // Print SharpPcap version
            string ver = SharpPcap.Version.VersionString;
            Console.WriteLine("SharpPcap {0}", ver);

            // Retrieve the device list
            var devices = CaptureDeviceList.Instance;

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
            foreach(var dev in devices)
            {
                /* Description */
                Console.WriteLine("{0}) {1} {2}", i, dev.Name, dev.Description);
                i++; 
            }

            Console.WriteLine();
            Console.Write("-- Please choose a device to capture: ");
            i = int.Parse( Console.ReadLine() );

            var device = devices[i];

            // Register our handler function to the 'packet arrival' event
            device.OnPacketArrival +=
                new PacketArrivalEventHandler( device_OnPacketArrival );

            // Open the device for capturing
            int readTimeoutMilliseconds = 1000;
            if(device is WinPcapDevice)
            {
                var winPcap = device as WinPcapDevice;
                winPcap.Open(OpenFlags.DataTransferUdp | OpenFlags.NoCaptureLocal, readTimeoutMilliseconds);
            }
            else if (device is LibPcapLiveDevice)
            {
                var livePcapDevice = device as LibPcapLiveDevice;
                livePcapDevice.Open(DeviceMode.Promiscuous, readTimeoutMilliseconds);
            }
            else
            {
                throw new System.InvalidOperationException("unknown device type of " + device.GetType().ToString());
            }

            Console.WriteLine();
            Console.WriteLine("-- Listening on {0} {1}, hit 'Enter' to stop...",
                device.Name, device.Description);

            tcpConnectionManager = new TcpConnectionManager();
            tcpConnectionManager.OnConnectionFound += HandleTcpConnectionManagerOnConnectionFound;

            // Start the capturing process
            device.StartCapture();

            // Wait for 'Enter' from the user.
            Console.ReadLine();

            // Stop the capturing process
            device.StopCapture();

            Console.WriteLine("-- Capture stopped.");

            // Print out the device statistics
            Console.WriteLine(device.Statistics.ToString());

            // Close the pcap device
            device.Close();
        }

        static ValueTuple<string, string, string, long>[] PortColors = {
            (AnsiEscapeSequences.White, AnsiEscapeSequences.CyanBackground, "HTTPS", 443),
            (AnsiEscapeSequences.White, AnsiEscapeSequences.GreenBackground, "DNS", 53),
            (AnsiEscapeSequences.White, AnsiEscapeSequences.RedBackground, "SSH", 22)
        };

        static void HandleTcpConnectionManagerOnConnectionFound (TcpConnection c)
        {
            string fgColorAnsi = AnsiEscapeSequences.Black;
            string bgColorAnsi = AnsiEscapeSequences.Reset;
            string portName = "UNKNOWN";

            foreach (var x in PortColors)
            {
                long serverPort, clientPort;

                if (c.Flows[0].port == x.Item4)
                {
                    serverPort = c.Flows[0].port;
                    clientPort = c.Flows[1].port;
                    fgColorAnsi = x.Item1;
                    bgColorAnsi = x.Item2;
                    portName = x.Item3;
                }
                else if (c.Flows[1].port == x.Item4)
                {
                    serverPort = c.Flows[1].port;
                    clientPort = c.Flows[0].port;
                    fgColorAnsi = x.Item1;
                    bgColorAnsi = x.Item2;
                    portName = x.Item3;
                }
            }

            Console.WriteLine("opened {0}{1}{2}{3} {4} :{5} - {6} :{7}",
                              fgColorAnsi,
                              bgColorAnsi,
                              portName,
                              AnsiEscapeSequences.Reset,
                              c.Flows[0].address,
                              c.Flows[0].port,
                              c.Flows[1].address,
                              c.Flows[1].port);
            Console.WriteLine("{0} connections", tcpConnectionManager.Connections.Count);

            // receive notifications when the connection is closed
            c.OnConnectionClosed += HandleCOnConnectionClosed;
        }

        static void HandleCOnConnectionClosed (PosixTimeval timeval,
                                               TcpConnection c,
                                               PacketDotNet.TcpPacket tcp,
                                               TcpConnection.CloseType closeType)
        {
            Console.WriteLine("closed {0}:{1} - {2}:{3} due to {4}",
                              c.Flows[0].address,
                              c.Flows[0].port,
                              c.Flows[1].address,
                              c.Flows[1].port,
                              closeType);

            Console.WriteLine("{0} connections", tcpConnectionManager.Connections.Count);
        }

        /// <summary>
        /// Prints the time and length of each received packet
        /// </summary>
        private static void device_OnPacketArrival(object sender, CaptureEventArgs e)
        {
#if false
            var time = e.Packet.Timeval.Date;
            var len = e.Packet.Data.Length;
            Console.WriteLine("{0}:{1}:{2},{3} Len={4}",
                time.Hour, time.Minute, time.Second, time.Millisecond, len);
            Console.WriteLine(e.Packet.ToString());
#endif

            var packet = PacketDotNet.Packet.ParsePacket(e.Packet.LinkLayerType,
                                                         e.Packet.Data);

            var tcpPacket = packet.Extract<TcpPacket>();

            // only pass tcp packets to the tcpConnectionManager
            if(tcpPacket != null)
            {
                tcpConnectionManager.ProcessPacket(e.Packet.Timeval,
                                                   tcpPacket);
            }
        }
    }
}
