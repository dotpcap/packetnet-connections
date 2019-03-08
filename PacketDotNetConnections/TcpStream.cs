/*
 * Copyright 2019 Chris Morgan <chmorgan@gmail.com>
 *
 * GPLv3 licensed, see LICENSE for full text
 * Commercial licensing available
 */

using System.Collections.Generic;
using System.IO;

namespace PacketDotNet.Connections
{
    /// <summary>
    /// The stream operates on a few basic principles
    /// - Packets are added at the end and the stream is resized as new packets arrive
    /// - Packet data length is tracked such that advancing to the next packet is possible
    /// - When enough data is available for removal, ie. the position has advanced beyond
    ///   the start of the stream by a number of bytes, the stream is re-created
    ///   starting at the current packet
    /// </summary>
    public class TcpStream : MemoryStream
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);        

        private class PacketInfo
        {
            public long seq;
            public long length;
            public long offset;

            // copy constructor
            public PacketInfo(PacketInfo src)
            {
                seq = src.seq;
                length = src.length;
                offset = src.offset;
            }

            // _offset is the offset into the stream where this packet
            // starts
            public PacketInfo(TcpPacket packet, long _offset)
            {
                seq = packet.SequenceNumber;
                length = packet.PayloadData.Length;

                offset = _offset;
            }

            public PacketInfo(long Seq, long Length, long Offset)
            {
                this.seq = Seq;
                this.length = Length;
                this.offset = Offset;
            }
        }
        LinkedList<PacketInfo> packets;        

        private TcpPacket _firstPacket;

        /// <summary>
        /// The first is kept for its src/dst ip and port as that identifies a stream
        /// </summary>
        public TcpPacket FirstPacket
        {
            get { return _firstPacket; }
        }

        public TcpStream() : base()
        {
            _firstPacket = null;
            packets = new LinkedList<PacketInfo>();
        }

        /// <summary>
        /// add a new packet to the end of our packets list
        /// NOTE: we drop 0 length packets as there is no easy way to
        ///       represent a unique offset inside of a zero length
        ///       packet
        /// </summary>
        /// <param name="newPacket">
        /// A <see cref="TcpPacket"/>
        /// </param>
        public void AppendPacket(TcpPacket newPacket)
        {
#if PERFORMANCE_CRITICAL_LOGGING
            log.Debug("");
#endif

            // if we are initialized we should validate the new packet
            if(FirstPacket != null)
            {
                var newIpPacket = (IPPacket)newPacket.ParentPacket;
                var firstIpPacket = (IPPacket)FirstPacket.ParentPacket;

                // does this packets settings match that of the stream?
                if(!firstIpPacket.SourceAddress.Equals(newIpPacket.SourceAddress) ||
                   !FirstPacket.SourcePort.Equals(newPacket.SourcePort) ||
                   !firstIpPacket.DestinationAddress.Equals(newIpPacket.DestinationAddress) ||
                   !FirstPacket.DestinationPort.Equals(newPacket.DestinationPort))
                {
                    string error = "newPacket does not belong to this stream, FirstPacket was " +
                              FirstPacket.ToString() + ", newPacket is " + newPacket.ToString();
                    log.Error(error);
                    throw new TcpStreamPacketNotPartOfStreamException(error);
                }
            } else // store the first packet
            {
#if PERFORMANCE_CRITICAL_LOGGING
                log.DebugFormat("storing FirstPacket of {0}", newPacket);
#endif
                _firstPacket = newPacket;
            }

            // NOTE: we purposely retrieve these values here for optimization purposes
            var payloadData = newPacket.PayloadData;
            int payloadLength = payloadData.Length;

#if PERFORMANCE_CRITICAL_LOGGING
            log.DebugFormat("payloadLength {0}", payloadLength);
#endif

            // we drop zero length packets, see the documentation for this method
            if(payloadLength == 0)
            {
#if PERFORMANCE_CRITICAL_LOGGING
                log.Debug("dropping zero length packet");
#endif
                return;
            }

            // add the packet info to the end of the array
            var newPacketInfo = new PacketInfo(newPacket.SequenceNumber,
                                               payloadLength,
                                               base.Length);
            packets.AddLast(newPacketInfo);

            // get the current position
            long position = base.Position;

            // seek to the end of the stream
            base.Seek(0, SeekOrigin.End);

            // write the packet data to the end of the stream
            base.Write(payloadData, 0, payloadLength);

            // seek back to the previous position
            base.Position = position;

#if false
            log.Debug("Packet list:");
            LinkedListNode<PacketInfo> pos = packets.First;
            while(pos != null)
            {
                log.DebugFormat("\tseq: {0}, length: {1}, offset: {2}",
                                pos.Value.seq, pos.Value.length, pos.Value.offset);
                pos = pos.Next;
            }

            pos = GetCurrentPosition();
            if(pos != null)
            {
                log.DebugFormat("\tCurrent packet is seq: {0}, length: {1}, offset: {2}",
                                    pos.Value.seq, pos.Value.length, pos.Value.offset);
            }
#endif
        }

        private LinkedListNode<PacketInfo> GetCurrentPosition()
        {
#if PERFORMANCE_CRITICAL_LOGGING
            log.DebugFormat("base.Length is {0}, base.Position {1}",
                            base.Length, base.Position);
#endif
            LinkedListNode<PacketInfo> pos = packets.First;
            while(pos != null)
            {
#if PERFORMANCE_CRITICAL_LOGGING
                log.DebugFormat("pos.Value.offset {0}, pos.Value.length {1}",
                                pos.Value.offset, pos.Value.length);
#endif
                // if our position is less than the last byte in 'pos' then
                // 'pos' is the current packet
                if(base.Position < (pos.Value.offset + pos.Value.length))
                {
                    break;
                }

                pos = pos.Next;
            }

            return pos;
        }

        /// <summary>
        /// advances to the next available packet or the end of the stream if
        //  there is no next packet
        /// </summary>
        /// <returns>
        /// A <see cref="System.Boolean"/> if the advance worked, false if there is no next packet
        /// </returns>
        public bool AdvanceToNextPacket()
        {
#if PERFORMANCE_CRITICAL_LOGGING
            log.Debug("");
#endif

            LinkedListNode<PacketInfo> currentPacket = GetCurrentPosition();

            // if we have no current packet we are done, there is no next packet
            if(currentPacket == null)
            {
                return false;
            }

            // if we have no next packet seek to the byte following the end of the stream
            if(currentPacket.Next == null)
            {
#if PERFORMANCE_CRITICAL_LOGGING
                log.DebugFormat("no next packet, seeking to the position after the current packet, {0}",
                                currentPacket.Value.offset + currentPacket.Value.length);
#endif
                base.Seek(0, SeekOrigin.End);
#if PERFORMANCE_CRITICAL_LOGGING
                log.DebugFormat("ended up at position {0}", base.Position);
#endif
                return false;
            }

            currentPacket = currentPacket.Next; // advance to the next packet
            base.Position = currentPacket.Value.offset; // update the position based on the next packet
            return true;
        }

        /// <summary>
        /// Return a new TcpStream that is the same as the previous stream but has the packets
        /// we've already read trimmed off of the list
        /// </summary>
        /// <returns>
        /// A <see cref="TcpStream"/>
        /// </returns>
        public TcpStream TrimUnusedPackets()
        {
#if PERFORMANCE_CRITICAL_LOGGING
            log.Debug("");
#endif

            // create a new stream
            TcpStream newStream = new TcpStream();

            // if we have no first packet then we can exit here
            if(FirstPacket == null)
            {
#if PERFORMANCE_CRITICAL_LOGGING
                log.Debug("FirstPacket == null, returning empty TcpStream");
#endif
                return newStream;
            }

            // copy the first packet variable
            newStream._firstPacket = FirstPacket;

            LinkedListNode<PacketInfo> currentPacket = GetCurrentPosition();

            // special case for when we are at the end of the last packet
            // in which case we have nothing to do
            if(currentPacket == null)
            {
#if PERFORMANCE_CRITICAL_LOGGING
                log.Debug("currentPacket == null, returning empty newStream");
#endif
                return newStream;
            }

            // the offset of the currentPacket is the number of bytes we can discard
            long bytesToDiscard = currentPacket.Value.offset;

#if PERFORMANCE_CRITICAL_LOGGING
            log.DebugFormat("bytesToDiscard {0}", bytesToDiscard);
#endif

            // copy the PacketInfo entries from the current stream to the new one
            LinkedListNode<PacketInfo> pos = currentPacket;
            while(pos != null)
            {
#if PERFORMANCE_CRITICAL_LOGGING
                log.DebugFormat("adding packet of size {0}", pos.Value.length);
#endif

                // copy the packet info from the current stream
                PacketInfo newPacketInfo = new PacketInfo(pos.Value);

                // adjust its offset
                newPacketInfo.offset -= bytesToDiscard;

                // add it to the new stream
                newStream.packets.AddLast(newPacketInfo);

#if PERFORMANCE_CRITICAL_LOGGING
                log.DebugFormat("newStream.Length {0}", newStream.Length);
#endif

                // advance to the next packet in the current stream
                pos = pos.Next;
            }

#if PERFORMANCE_CRITICAL_LOGGING
            log.DebugFormat("after importing all packets newStream.Length {0}", newStream.Length);
#endif

            // now write the bytes from the old stream
            // NOTE: we cannot use originalBytes.Length because MemoryStream.GetBuffer() returns
            //       the internal buffer that can be larger than the actual number of bytes in the stream
            byte[] originalBytes = base.GetBuffer();
            newStream.Write(originalBytes, (int)bytesToDiscard, (int)(base.Length - bytesToDiscard));

            // set the proper position in the newStream
#if PERFORMANCE_CRITICAL_LOGGING
            log.DebugFormat("base.Position {0}", base.Position);
#endif
            newStream.Position = base.Position - bytesToDiscard;

            //FIXME: for debugging
#if false
            log.DebugFormat("trimmed size from {0} down to {1}, starting packets, {2}, ending packets {3}, removed {4} packets",
                            base.Length, newStream.Length,
                            packets.Count, newStream.packets.Count,
                            (packets.Count - newStream.packets.Count));
            log.DebugFormat("original position of {0} is position of {1} in the newStream",
                            base.Position, newStream.Position);
#endif

            return newStream;
        }

        /// <summary>
        /// ToString overrride
        /// </summary>
        /// <returns>
        /// A <see cref="System.String"/>
        /// </returns>
        public override string ToString()
        {
            return string.Format("[TcpStream FirstPacket: {0}", FirstPacket);
        }
    }
}
