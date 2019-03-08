/*
 * Copyright 2019 Chris Morgan <chmorgan@gmail.com>
 *
 * GPLv3 licensed, see LICENSE for full text
 * Commercial licensing available
 */
using System;
using System.Collections.Generic;
using SharpPcap;

namespace PacketDotNet.Connections
{
    /// <summary>
    /// Represents a tcp connection
    /// </summary>
    public class TcpConnection
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Flows for this connection
        /// </summary>
        public List<TcpFlow> Flows;

        /// <summary>
        /// States of a connection
        /// </summary>
        public enum ConnectionStates
        {
            /// <summary>
            /// Open
            /// </summary>
            Open,

            /// <summary>
            /// Waiting for second fin/ack
            /// </summary>
            WaitingForSecondFinAck,

            /// <summary>
            /// Waiting for final ack
            /// </summary>
            WaitingForFinalAck,

            /// <summary>
            /// Closed
            /// </summary>
            Closed
        }

        /// <summary>
        /// Connection state
        /// </summary>
        public ConnectionStates ConnectionState
        {
            get; set;
        }

        /// <summary>
        /// Does the TcpPacket match with this connection?
        /// </summary>
        /// <param name="tcp">
        /// A <see cref="TcpPacket"/>
        /// </param>
        /// <returns>
        /// A <see cref="TcpFlow"/>
        /// </returns>
        public TcpFlow IsMatch(TcpPacket tcp)
        {
            var ip = tcp.ParentPacket as IPPacket;

            // match a packet from/to either end of the link
            //
            // NOTE: Could use a hash of the ip src/dst, tcp src/dst port
            // but this wouldn't be unique, if the ports were reversed between
            // src/dst the same hash would be generated. Generating a unique hash
            // is possible by applying a unique prime to either the src or dst
            // fields but that would require us to keep two hashes so we could
            // quickly match the tcp packet hash to either of the two hashes
            // generated
            if(
               ((Flows[0].port.Equals(tcp.SourcePort)) &&
               (Flows[0].address.Equals(ip.SourceAddress)) &&
               (Flows[1].port.Equals(tcp.DestinationPort)) &&
               (Flows[1].address.Equals(ip.DestinationAddress))))
            {
                return Flows[0];
            }

            if(
               ((Flows[1].port.Equals(tcp.SourcePort)) &&
               (Flows[1].address.Equals(ip.SourceAddress)) &&
               (Flows[0].port.Equals(tcp.DestinationPort)) &&
               (Flows[0].address.Equals(ip.DestinationAddress))))
            {
                return Flows[1];
            }

            return null;
        }

        /// <summary>
        /// Generate the hash of an ip and tcp packet
        /// </summary>
        /// <param name="ip">
        /// A <see cref="IpPacket"/>
        /// </param>
        /// <param name="tcp">
        /// A <see cref="TcpPacket"/>
        /// </param>
        /// <returns>
        /// A <see cref="System.Int32"/>
        /// </returns>
        public static int GenerateConnectionHash(IPPacket ip, TcpPacket tcp)
        {
            int hash = ip.SourceAddress.GetHashCode() ^
                       ip.DestinationAddress.GetHashCode() ^
                       tcp.SourcePort.GetHashCode() ^
                       tcp.DestinationPort.GetHashCode();
            return hash;
        }

        private TimeSpan timeout = new TimeSpan(0, 10, 0);

        /// <summary>
        /// Time between packets before a connection will
        /// be considered dead
        /// </summary>
        public TimeSpan Timeout
        {
            get
            {
                return timeout;
            }

            set
            {
                timeout = value;
                closeTimer.Interval = Timeout.TotalMilliseconds;
            }
        }

        /// <summary>
        /// Time when the last packet was received
        /// </summary>
        public DateTime LastPacketReceived = DateTime.Now;

        /// <summary>
        /// Used to close stale connections
        /// </summary>
        public System.Timers.Timer closeTimer;

        /// <summary>
        /// Returns true if this connection has timed out
        /// </summary>
        public bool HasTimedOut
        {
            get
            {
                if((DateTime.Now - LastPacketReceived) > Timeout)
                    return true;
                return false;
            }
        }

        /// <summary>
        /// Find a flow, if one exists, from a tcp packet, or null
        /// </summary>
        /// <param name="tcp">
        /// A <see cref="TcpPacket"/>
        /// </param>
        /// <returns>
        /// A <see cref="TcpFlow"/>
        /// </returns>
        public TcpFlow FindFlow(TcpPacket tcp)
        {
            TcpFlow foundFlow = null;

            foreach(var flow in Flows)
            {
                if(flow.Matches(tcp))
                {
                    foundFlow = flow;
                    break;
                }
            }

            return foundFlow;
        }

        /// <summary>
        /// Event when a packet is received
        /// </summary>
        public delegate void PacketReceivedDelegate(PosixTimeval timeval, TcpConnection connection, TcpFlow flow, TcpPacket tcp);

        /// <summary>
        /// Delegate called upon packet reception
        /// </summary>
        public event PacketReceivedDelegate OnPacketReceived;

        /// <summary>
        /// Determines the type of close event
        /// </summary>
        public enum CloseType
        {
            /// <summary>
            /// Flows are closed
            /// </summary>
            FlowsClosed,

            /// <summary>
            /// Connection timeout
            /// </summary>
            ConnectionTimeout
        }

        /// <summary>
        /// Reports a connection closed event
        /// </summary>
        public delegate void ConnectionClosedDelegate(PosixTimeval timeval,
                                                      TcpConnection connection,
                                                      TcpPacket tcp,
                                                      CloseType closeType);

        /// <summary>
        /// Delegate called upon packet reception
        /// </summary>
        public event ConnectionClosedDelegate OnConnectionClosed;

        /// <summary>
        /// Create a tcp
        /// </summary>
        /// <param name="tcp">
        /// A <see cref="TcpPacket"/>
        /// </param>
        public TcpConnection (TcpPacket tcp)
        {
            ConnectionState = ConnectionStates.Open;

            closeTimer = new System.Timers.Timer();
            closeTimer.Interval = Timeout.TotalMilliseconds;
            closeTimer.Elapsed += HandleCloseTimerElapsed;

            Flows = new List<TcpFlow>();
            var ip = tcp.ParentPacket as IPPacket;

            var flowA = new TcpFlow(ip.SourceAddress, tcp.SourcePort);
            Flows.Add(flowA);

            var flowB =  new TcpFlow(ip.DestinationAddress, tcp.DestinationPort);
            Flows.Add(flowB);

            // start the close timer
            closeTimer.Enabled = true;
        }

        void HandleCloseTimerElapsed (object sender, System.Timers.ElapsedEventArgs e)
        {
            log.DebugFormat("");

            if(OnConnectionClosed != null)
            {
                OnConnectionClosed(null, this, null, CloseType.ConnectionTimeout);
            }
        }

        internal void HandlePacket(PosixTimeval timeval, TcpPacket tcp, TcpFlow flow)
        {
            log.DebugFormat("timeval {0}, tcp {1}, flow {2}",
                            timeval,
                            tcp,
                            flow);

            LastPacketReceived = DateTime.Now;

            // reset the timer
            closeTimer.Interval = Timeout.TotalMilliseconds;

            switch(ConnectionState)
            {
            case ConnectionStates.Open:
                if(tcp.Fin && tcp.Ack)
                {
                    ConnectionState = ConnectionStates.WaitingForSecondFinAck;
                }
                break;

            case ConnectionStates.WaitingForSecondFinAck:
                if(tcp.Fin && tcp.Ack)
                {
                    ConnectionState = ConnectionStates.WaitingForFinalAck;
                }
                break;
            case ConnectionStates.WaitingForFinalAck:
                if(tcp.Ack)
                {
                    ConnectionState = ConnectionStates.Closed;

                    // report connection closure
                    if(OnConnectionClosed != null)
                    {
                        OnConnectionClosed(timeval, this, tcp, CloseType.FlowsClosed);
                    }
                }
                break;
            }

            if(OnPacketReceived != null)
                OnPacketReceived(timeval, this, flow, tcp);
        }

        /// <summary>
        /// ToString() override
        /// </summary>
        /// <returns>
        /// A <see cref="System.String"/>
        /// </returns>
        public override string ToString ()
        {
            return string.Format ("[TcpConnection: HasTimedOut={0}, Flow[0] {1}, Flow[1] {2}]",
                                  HasTimedOut,
                                  Flows[0],
                                  Flows[1]);
        }
    }
}
