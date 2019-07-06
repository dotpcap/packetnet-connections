/*
 * Copyright 2019 Chris Morgan <chmorgan@gmail.com>
 *
 * GPLv3 licensed, see LICENSE for full text
 * Commercial licensing available
 */

using System;
using System.Collections.Generic;
using SharpPcap;

namespace PacketDotNet.Connections.Tools
{
    /// <summary>
    /// Class that measures the time between the syn and syn-ack of a tcp connection
    /// </summary>
    public class ConnectionStatistics
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Key time values when establishing a tcp connection
        /// </summary>
        public class ConnectionTimes
        {
            /// <summary>
            /// Time when the syn packet was received
            /// </summary>
            public PosixTimeval synTime;

            /// <summary>
            /// Flow for the tcp client, the side establishing the connection
            /// </summary>
            public TcpFlow clientFlow;

            /// <summary>
            /// The time the ack to the syn was received
            /// </summary>
            public PosixTimeval synAckTime;

            /// <summary>
            /// Flow for the tcp server
            /// </summary>
            public TcpFlow serverFlow;

            /// <summary>
            /// Override for ToString
            /// </summary>
            /// <returns>
            /// A <see cref="System.String"/>
            /// </returns>
            public override string ToString ()
            {
                TimeSpan? synAckTimeSpan = null;
                if((synTime != null) && (synAckTime != null))
                {
                    synAckTimeSpan = synAckTime.Date - synTime.Date;
                }

                return string.Format ("[ConnectionTimes synTime '{0}', synAckTime '{1}', elapsed '{2}']",
                                      synTime, synAckTime, synAckTimeSpan);
            }
        }

        private Dictionary<TcpConnection, ConnectionTimes> connectionDictionary = new Dictionary<TcpConnection, ConnectionTimes>();

        #region Measurements
        /// <summary>
        /// Delegate for measurement received
        /// </summary>
        public delegate void MeasurementFound(ConnectionStatistics connectionStatistics,
                                              TcpConnection c,
                                              ConnectionTimes connectionTimes);

        /// <summary>
        /// Called when a connection measurement has been found
        /// </summary>
        public event MeasurementFound OnMeasurementFound;
        #endregion

        #region events
        /// <summary>
        /// Types of events
        /// </summary>
        public enum EventTypes
        {
            /// <summary>
            /// Two syn packets were found
            /// </summary>
            DuplicateSynFound,

            /// <summary>
            /// Two syn/ack packets were found
            /// </summary>
            DuplicateSynAckFound
        }

        /// <summary>
        /// Connection event is found
        /// </summary>
        public delegate void MeasurementEvent(ConnectionStatistics connectionStatistics,
                                              TcpConnection c,
                                              EventTypes eventType);

        /// <summary>
        /// Called when measurement events occur
        /// </summary>
        public event MeasurementEvent OnMeasurementEvent;
        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        public ConnectionStatistics()
        {
        }

        /// <summary>
        /// Begin monitoring a given connection
        /// </summary>
        /// <param name="c">
        /// A <see cref="TcpConnection"/>
        /// </param>
        public void MonitorConnection(TcpConnection c)
        {
            connectionDictionary[c] = new ConnectionTimes();
            c.OnPacketReceived += HandleCOnPacketReceived;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="timeval">
        /// A <see cref="PosixTimeval"/>
        /// </param>
        /// <param name="connection">
        /// A <see cref="TcpConnection"/>
        /// </param>
        /// <param name="flow">
        /// A <see cref="TcpFlow"/>
        /// </param>
        /// <param name="tcp">
        /// A <see cref="TcpPacket"/>
        /// </param>
        protected void HandleCOnPacketReceived (PosixTimeval timeval, TcpConnection connection, TcpFlow flow, TcpPacket tcp)
        {
            // look up the ConnectionStatistics
            var connectionStatistics = connectionDictionary[connection];

            if(tcp.Synchronize && !tcp.Acknowledgment)
            {
                if(connectionStatistics.clientFlow != null)
                {
                    OnMeasurementEvent(this, connection, EventTypes.DuplicateSynFound);
                    return;
                }

                connectionStatistics.clientFlow = flow;
                connectionStatistics.synTime = timeval;
            } else if(tcp.Synchronize && tcp.Acknowledgment)
            {
                if(connectionStatistics.serverFlow != null)
                {
                    OnMeasurementEvent(this, connection, EventTypes.DuplicateSynAckFound);
                    return;
                }

                connectionStatistics.serverFlow = flow;
                connectionStatistics.synAckTime = timeval;
            }

            // if we have both a syn and a syn-ack time then report that
            // we have a valid measurement
            if((connectionStatistics.synTime != null) &&
               (connectionStatistics.synAckTime != null))
            {
                OnMeasurementFound(this, connection, connectionStatistics);

                // unregister the handler since we found our syn-syn/ack time
                connection.OnPacketReceived -= HandleCOnPacketReceived;
            }
       }
    }
}