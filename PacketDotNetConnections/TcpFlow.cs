/*
 * Copyright 2019 Chris Morgan <chmorgan@gmail.com>
 *
 * GPLv3 licensed, see LICENSE for full text
 * Commercial licensing available
 */
using System.Net;
using SharpPcap;

namespace PacketDotNet.Connections
{
    /// <summary>
    /// Tracks one endpoint of a tcp connection
    /// </summary>
    public class TcpFlow
    {
        /// <summary>
        /// IP Address for this flow
        /// </summary>
        public IPAddress address;

        /// <summary>
        /// Port
        /// </summary>
        public int port;

        /// <summary>
        /// Acked sequence number
        /// </summary>
        public int? ack;

        /// <summary>
        /// Sequence number
        /// </summary>
        public int? seq;

        /// <summary>
        /// Whether a flow is open
        /// </summary>
        public bool IsOpen
        {
            get; set;
        }

        /// <summary>
        /// Event when packet is received
        /// </summary>
        public delegate void PacketReceivedDelegate(PosixTimeval timeval, TcpPacket tcp, TcpConnection connection, TcpFlow flow);

        /// <summary>
        /// Delegate called upon packet reception
        /// </summary>
        public event PacketReceivedDelegate OnPacketReceived;

        /// <summary>
        /// Flow is closed
        /// </summary>
        public delegate void FlowClosedDelegate(PosixTimeval timeval, TcpPacket tcp, TcpConnection connection, TcpFlow flow);

        /// <summary>
        /// Delegate called upon flow close
        /// </summary>
        public event FlowClosedDelegate OnFlowClosed;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="address">
        /// A <see cref="IPAddress"/>
        /// </param>
        /// <param name="port">
        /// A <see cref="System.Int32"/>
        /// </param>
        public TcpFlow (IPAddress address, int port)
        {
            IsOpen = true;
            this.address = address;
            this.port = port;
        }

        internal void HandlePacket(PosixTimeval timeval, TcpPacket tcp, TcpConnection connection)
        {
            if(OnPacketReceived != null)
            {
                OnPacketReceived(timeval, tcp, connection, this);
            }

            // look for disconnection
            if(tcp.Fin == true)
            {
                IsOpen = false;

                if(OnFlowClosed != null)
                {
                    OnFlowClosed(timeval, tcp, connection, this);
                }
            }
        }

        /// <summary>
        /// Returns true if this packet matches this TcpFlow
        /// </summary>
        /// <param name="tcp">
        /// A <see cref="TcpPacket"/>
        /// </param>
        /// <returns>
        /// A <see cref="System.Boolean"/>
        /// </returns>
        public bool Matches(TcpPacket tcp)
        {
            var ip = tcp.ParentPacket as IPPacket;

            if((tcp.SourcePort == port) &&
               (ip.SourceAddress.Equals(address)))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// ToString override
        /// </summary>
        /// <returns>
        /// A <see cref="System.String"/>
        /// </returns>
        public override string ToString ()
        {
            return string.Format ("[TcpFlow address {0}, port {1}]",
                                  address, port);
        }

        /// <summary>
        /// Hash code
        /// </summary>
        /// <returns>
        /// A <see cref="System.Int32"/>
        /// </returns>
        public override int GetHashCode ()
        {
            int hashCode = address.GetHashCode() ^ port.GetHashCode();
            return hashCode;
        }
    }
}

