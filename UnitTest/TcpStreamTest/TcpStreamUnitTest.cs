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
    public class TcpStreamUnitTest
    {
        private string captureFN = "tcp_stream_test.pcap";
        private string capturePre = "../../../captureFiles/";
        private string captureFilename;

        [Fact]
        public void TestTcpStream()
        {
            Console.WriteLine(Environment.CurrentDirectory);
            captureFilename = capturePre + captureFN;

            // open the offline file
            var dev = new CaptureFileReaderDevice(captureFilename);
            dev.Open();
            Assert.True(dev != null, "failed to open " + captureFilename);

            TcpStream tcpStream = new TcpStream();

            int i = 1; // to match the packet numbering in wireshark
            RawCapture rawCapture;
            while((rawCapture = dev.GetNextPacket()) != null)
            {
                var p = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);
                var tcpPacket = p.Extract(typeof(TcpPacket)) as TcpPacket;

                Console.WriteLine("{0}", tcpPacket.ToString());
        #if false
                Console.WriteLine("packet_header.len is {0}", p.Length);
                Console.WriteLine("{0}", p.ToString());

                int x;
                for(x = 0; x < packet_header.len; x++)
                {
                    if((x % 16) == 0)
                        printf("\n");
                    printf("%02x ", pPacket[x]);
                }
                printf("\n");
        #endif

                Console.WriteLine("********************");
                Console.WriteLine("i == {0}", i);

                // and hand it off to the tcp_stream
                tcpStream.AppendPacket(tcpPacket);

                Console.WriteLine("Position {0}", tcpStream.Position);
                Console.WriteLine("Length   {0}", tcpStream.Length);

                // make sure the length and position is correct
                // and Seek() to the end of the stream
                if(i == 4)
                {
                    // we expect our length to be 38
                    long expectedLength = 38;
                    Assert.Equal(expectedLength, tcpStream.Length);

                    // advance through these bytes
                    tcpStream.Seek(0, System.IO.SeekOrigin.End);

                    // make sure that our position is correct
                    Assert.Equal(expectedLength, tcpStream.Position);
                }

                if(i == 5)
                {
                    // read the first 26 bytes into a memory stream
                    // these are header bytes, we don't really want them
                    int bytesToRead = 26;
                    byte[] dataBuffer1 = new byte[bytesToRead];
                    tcpStream.Read(dataBuffer1, 0, dataBuffer1.Length);                   

                    /////////////////////////////////////
                    // Step two, reading at the current location

                    // save the position
                    long position = tcpStream.Position;

                    // read bytes at that location
                    bytesToRead = 14;
                    byte[] dataBuffer2 = new byte[bytesToRead];
                    tcpStream.Read(dataBuffer2, 0, dataBuffer2.Length);

                    System.Text.ASCIIEncoding enc = new System.Text.ASCIIEncoding();
                    string actualString = enc.GetString(dataBuffer2, 0, dataBuffer2.Length);
                    Console.WriteLine("actualString '{0}'", actualString);

                    // expected string
                    string expectedString = "diffie-hellman";

                    // do our strings match?
                    Assert.Equal(expectedString, actualString);

                    // make sure our position advanced by the number of bytes we read
                    Assert.Equal((position + bytesToRead), tcpStream.Position);
                }

                Console.WriteLine("i = {0}", i);
                i++;
            }
            Console.WriteLine();

            dev.Close();
        }

        [Fact]
        // test that we can seek a tcpStream beyond the length of the stream
        // then back into the range of the stream and then read
        // a known value
        public void TestTcpStreamSeeking()
        {
            captureFilename = capturePre + captureFN;

            // open the offline file
            var dev = new CaptureFileReaderDevice(captureFilename);
            dev.Open();
            Assert.True(dev != null, "failed to open " + captureFilename);

            TcpStream tcpStream = new TcpStream();

            int i = 1; // to match the packet numbering in wireshark
            RawCapture rawCapture;
            while((rawCapture = dev.GetNextPacket()) != null)
            {
                var p = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);
                var tcpPacket = p.Extract(typeof(TcpPacket)) as TcpPacket; 

                // we only want the 4th packet since that is the first packet with a payload
                if(i == 4)
                {
                    tcpStream.AppendPacket(tcpPacket);
                }

                i++;
            }

            dev.Close();

            // what is the length of the stream?
            long length = tcpStream.Length;
            Console.WriteLine("length is {0}", length);

            // seek to the end of the stream
            tcpStream.Seek(0, System.IO.SeekOrigin.End); 

            // retrieve the position
            long position = tcpStream.Position;
            Console.WriteLine("position is {0}", position);

            // seek to the start of the stream
            tcpStream.Seek(0, System.IO.SeekOrigin.Begin);

            // retrieve the position
            long position2 = tcpStream.Position;
            Console.WriteLine("position2 is {0}", position2);
        }

        // test that the TcpStream properly puts the contents of the packets
        // into the stream
        [Fact]
        public void testTcpStreamRead()
        {
            captureFilename = capturePre + captureFN;

            // open the offline file
            var dev = new CaptureFileReaderDevice(captureFilename);
            dev.Open();
            Assert.True(dev != null, "failed to open " + captureFilename);

            TcpStream tcpStream = new TcpStream();

            int i = 1; // to match the packet numbering in wireshark
            RawCapture rawCapture;
            while((rawCapture = dev.GetNextPacket()) != null)
            {
                var p = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);
                var tcpPacket = p.Extract(typeof(TcpPacket)) as TcpPacket;
                // we only want the 4th packet since that is the first packet with a payload
                if(i == 4)
                {
                    tcpStream.AppendPacket(tcpPacket);
                }

                i++;
            }

            dev.Close();

            byte[] expectedData = {
                    0x53,
                    0x53,
                    0x48,
                    0x2d,
                    0x32,
                    0x2e,
                    0x30,
                    0x2d,
                    0x4f,
                    0x70,
                    0x65,
                    0x6e,
                    0x53,
                    0x53,
                    0x48,
                    0x5f,
                    0x34,
                    0x2e,
                    0x37,
                    0x70,
                    0x31,
                    0x20,
                    0x44,
                    0x65,
                    0x62,
                    0x69,
                    0x61,
                    0x6e,
                    0x2d,
                    0x38,
                    0x75,
                    0x62,
                    0x75,
                    0x6e,
                    0x74,
                    0x75,
                    0x31,
                    0x0a
                };

            byte[] actualData = new byte[expectedData.Length];

            tcpStream.Read(actualData, 0, actualData.Length);

            // see that the bytes match
            Assert.Equal(expectedData, actualData);

            // is our position where we expect it to be?
            int expected_position = 38;
            Assert.Equal(expected_position, tcpStream.Position);
        }

        [Fact]
        public void TestAdvanceToNextPacketandTrimUnusedPackets()
        {
            captureFilename = capturePre + captureFN;

            // open the offline file
            var dev = new CaptureFileReaderDevice(captureFilename);
            dev.Open();
            Assert.True(dev != null, "failed to open " + captureFilename);

            TcpStream tcpStream = new TcpStream();

            int i = 1; // to match the packet numbering in wireshark
            RawCapture rawCapture;
            while((rawCapture = dev.GetNextPacket()) != null)
            {
                var p = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);
                var tcpPacket = p.Extract(typeof(TcpPacket)) as TcpPacket;
                tcpStream.AppendPacket(tcpPacket);
                i++;
            }
            dev.Close();

            long expected_position;

            // verify our position is 0
            expected_position = 0;
            Assert.Equal(expected_position, tcpStream.Position);

            // advance to the next packet
            tcpStream.AdvanceToNextPacket();

            // is our current position correct
            expected_position = 38;
            Assert.Equal(expected_position, tcpStream.Position);

            // test that if we advance to the next packet and no packet is available that
            // we will end up at the end of the stream
            tcpStream.AdvanceToNextPacket();
            Assert.True(tcpStream.Position == tcpStream.Length, "stream position and length don't match");

            // now trim any unused packets
            tcpStream = tcpStream.TrimUnusedPackets();
        }

        [Fact]
        public void Performance()
        {
            captureFilename = capturePre + captureFN;

            // open the offline file
            var dev = new CaptureFileReaderDevice(captureFilename);
            dev.Open();
            Assert.True(dev != null, "failed to open " + captureFilename);

            TcpStream tcpStream = new TcpStream();

            int i = 1; // to match the packet numbering in wireshark
            RawCapture rawCapture;
            const int packetsToAppend = 100000;
            while((rawCapture = dev.GetNextPacket()) != null)
            {
                var p = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);
                var tcpPacket = p.Extract(typeof(TcpPacket)) as TcpPacket;

                for(int x = 0; x < packetsToAppend; x++)
                {
                    tcpStream.AppendPacket(tcpPacket);

                    // advance to the next packet
                    tcpStream.AdvanceToNextPacket();

                    tcpStream = tcpStream.TrimUnusedPackets();
                }

                i++;
            }
            dev.Close();            
        }
    }
}
