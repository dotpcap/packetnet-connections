/*
 * Copyright 2019 Chris Morgan <chmorgan@gmail.com>
 *
 * GPLv3 licensed, see LICENSE for full text
 * Commercial licensing available
 */
using System;

namespace PacketDotNet.Connections
{
    /// <summary>
    /// Base class for all tcp stream exceptions
    /// </summary>
    public class TcpStreamException : SystemException
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public TcpStreamException()
        {
        }

        /// <summary>
        /// Constructor with message
        /// </summary>
        /// <param name="message">
        /// A <see cref="System.String"/>
        /// </param>
        public TcpStreamException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Thrown when a packet that isn't part of a TcpStream is being added to a stream
    /// </summary>
    public class TcpStreamPacketNotPartOfStreamException : TcpStreamException
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public TcpStreamPacketNotPartOfStreamException()
        {
        }

        /// <summary>
        /// Constructor with message
        /// </summary>
        /// <param name="message">
        /// A <see cref="System.String"/>
        /// </param>
        public TcpStreamPacketNotPartOfStreamException(string message) : base(message)
        {
        }
    }
}
