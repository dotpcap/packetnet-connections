// Example showing the features of the assembly including
// tcp packet reassembly and http parsing, including decoding of
// gzipped or deflated contents
//
// Copyright 2019 Chris Morgan <chmorgan@gmail.com>

using System;
using PacketDotNet;
using PacketDotNet.Connections;
using PacketDotNet.Connections.Http;
using SharpPcap;

// Configure log4net using the .config file
[assembly: log4net.Config.XmlConfigurator(Watch = true)]
// This will cause log4net to look for a configuration file
// called TestApp.exe.config in the application base
// directory (i.e. the directory containing TestApp.exe)
// The config file will be watched for changes.

namespace HttpMonitorExample
{
    class MainClass
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static TcpConnectionManager tcpConnectionManager = new TcpConnectionManager();
        public static void Main(string[] args)
        {
            // Print SharpPcap version
            var ver = SharpPcap.Pcap.SharpPcapVersion;
            Console.WriteLine("SharpPcap {0}", ver);

            // Retrieve the device list
            var devices = SharpPcap.LibPcap.LibPcapLiveDeviceList.Instance;

            // If no devices were found print an error
            if (devices.Count < 1)
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
            foreach (SharpPcap.LibPcap.LibPcapLiveDevice dev in devices)
            {
                /* Description */
                Console.WriteLine("{0}) {1} {2}", i, dev.Name, dev.Description);
                i++;
            }

            Console.WriteLine();
            Console.Write("-- Please choose a device to capture: ");
            i = int.Parse(Console.ReadLine());

            ICaptureDevice device = devices[i];

            tcpConnectionManager.OnConnectionFound += HandleTcpConnectionManagerOnConnectionFound;

            // Register our handler function to the 'packet arrival' event
            device.OnPacketArrival +=
                new PacketArrivalEventHandler(device_OnPacketArrival);

            // Open the device for capturing
            int readTimeoutMilliseconds = 1000;
            device.Open(DeviceModes.Promiscuous, readTimeoutMilliseconds);

            Console.WriteLine();
            Console.WriteLine("-- Listening on {0} {1}, hit 'Enter' to stop...",
                device.Name, device.Description);

            // Start the capturing process
            device.StartCapture();

            // Wait for 'Enter' from the user.
            Console.ReadLine();

            // Stop the capturing process
            device.StopCapture();

            Console.WriteLine("-- Capture stopped.");
        }

        static void HandleTcpConnectionManagerOnConnectionFound (TcpConnection c)
        {
            var httpSessionWatcher = new HttpSessionWatcher(c,
                                                            OnHttpRequestFound,
                                                            OnHttpStatusFound,
                                                            OnHttpWatcherError);
        }

        /// <summary>
        /// Prints the time and length of each received packet
        /// </summary>
        private static void device_OnPacketArrival(object sender, PacketCapture e)
        {
#if false
            var time = e.Packet.Timeval.Date;
            var len = e.Packet.Data.Length;
            Console.WriteLine("{0}:{1}:{2},{3} Len={4}",
                time.Hour, time.Minute, time.Second, time.Millisecond, len);
            Console.WriteLine(e.Packet.ToString());
#endif
            var rawPacket = e.GetPacket();
            var p = PacketDotNet.Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);
            var tcpPacket = p.Extract<TcpPacket>();

            if(tcpPacket == null)
                return;

            log.Debug("passing packet to TcpConnectionManager");
            tcpConnectionManager.ProcessPacket(rawPacket.Timeval,
                                               tcpPacket);
        }

        private static void OnHttpRequestFound(HttpSessionWatcherRequestEventArgs e)
        {
            log.Debug("");

            // only display compressed messages
            if((e.Request.ContentEncoding == HttpMessage.ContentEncodings.Deflate) ||
               (e.Request.ContentEncoding == HttpMessage.ContentEncodings.Gzip))
            {
                //               log.Info(e.Request.ToString());
                Console.WriteLine(e.Request.ToString());
            }

            // NOTE: Regex on the url can be performed here on
            // e.Request.Url
        }

        private static void OnHttpStatusFound(HttpSessionWatcherStatusEventArgs e)
        {
            log.Debug("");

            // only display compressed messages
            if((e.Status.ContentEncoding == HttpMessage.ContentEncodings.Deflate) ||
               (e.Status.ContentEncoding == HttpMessage.ContentEncodings.Gzip))
            {
                //                log.Info(e.Status.ToString());
                Console.WriteLine(e.Status.ToString());
            }
        }

        private static void OnHttpWatcherError(string errorString)
        {
            log.Error("errorString " + errorString);
        }
    }
}

