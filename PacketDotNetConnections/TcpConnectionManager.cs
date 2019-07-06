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
    /// High level tcp connection manager
    /// </summary>
    public class TcpConnectionManager
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Amount of time before a connection is timed out in the case where
        /// no more packets have been seen from a given connection
        /// </summary>
        public TimeSpan ConnectionTimeout = new TimeSpan(0, 5, 0);

        /// <summary>
        /// Active connections
        /// </summary>
        public List<TcpConnection> Connections = new List<TcpConnection>();

        /// <summary>
        /// Delegate when connections are created
        /// </summary>
        public delegate void ConnectionFoundDelegate(TcpConnection c);

        /// <summary>
        /// Called when connections are found
        /// </summary>
        public event ConnectionFoundDelegate OnConnectionFound;

        /// <summary>
        /// Constructor
        /// </summary>
        public TcpConnectionManager ()
        {
        }



        /// <summary>
        /// Pass a packet to an existing connection if one is present
        /// or
        /// Create a new connection, notify the OnConnectionFound delegate and
        /// pass the packet to this new connection
        /// </summary>
        /// <param name="timeval">
        /// A <see cref="PosixTimeval"/>
        /// </param>
        /// <param name="tcp">
        /// A <see cref="TcpPacket"/>
        /// </param>
        public void ProcessPacket(PosixTimeval timeval, TcpPacket tcp)
        {
            TcpFlow foundFlow = null;
            TcpConnection foundConnection = null;

            // attempt to find the connection and flow that
            // this packet belongs to
            foreach(var c in Connections)
            {
                foundFlow = c.IsMatch(tcp);
                if(foundFlow != null)
                {
                    foundConnection = c;
                    break;
                }
            }

            TcpConnection connectionToUse;
            TcpFlow flowToUse;

            if(foundConnection == null)
            {
                log.Debug("foundConnection == null");

                if(tcp.Reset)
                {
                    log.Debug("creating new connection, RST flag is set");
                } else
                {
                    log.Debug("creating new connection, RST flag is not set");
                }

                // create a new connection
                connectionToUse = new TcpConnection(tcp);
                connectionToUse.OnConnectionClosed += HandleConnectionToUseOnConnectionClosed;
                connectionToUse.Timeout = ConnectionTimeout;

                // figure out which flow matches this tcp packet
                flowToUse = connectionToUse.FindFlow(tcp);

                // send notification that a new connection was found
                OnConnectionFound(connectionToUse);

                Connections.Add(connectionToUse);
            } else
            {
                // use the flows that we found
                connectionToUse = foundConnection;
                flowToUse = foundFlow;
            }

            // pass the packet to the appropriate connection
            connectionToUse.HandlePacket(timeval, tcp, flowToUse);

            // pass the packet to the appropriate flow
            flowToUse.HandlePacket(timeval, tcp, connectionToUse);
        }


        /// <summary>
        /// Handle closing connections by removing them from the list
        /// </summary>
        /// <param name="timeval">
        /// A <see cref="PosixTimeval"/>
        /// </param>
        /// <param name="connection">
        /// A <see cref="TcpConnection"/>
        /// </param>
        /// <param name="tcp">
        /// A <see cref="TcpPacket"/>
        /// </param>
        /// <param name="closeType">
        /// A <see cref="TcpConnection.CloseType"/>
        /// </param>
        void HandleConnectionToUseOnConnectionClosed (PosixTimeval timeval,
                                                      TcpConnection connection,
                                                      TcpPacket tcp,
                                                      TcpConnection.CloseType closeType)
        {
            // remove the connection from the list
            Connections.Remove(connection);
        }
    }
}
