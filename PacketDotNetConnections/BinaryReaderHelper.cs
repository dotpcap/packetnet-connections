/*
 * Copyright 2019 Chris Morgan <chmorgan@gmail.com>
 *
 * GPLv3 licensed, see LICENSE for full text
 * Commercial licensing available
 */

using System.IO;

namespace PacketDotNet.Connections
{
    /// <summary>
    /// Helper class used for reading strings from a BinaryReader
    /// </summary>
    public class BinaryReaderHelper
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);        

        /// <summary>
        /// Potential responses when reading strings
        /// </summary>
        public enum ReadNextLineResponses
        {
            /// <summary>
            /// we don't have enough bytes to have a string
            /// </summary>
            NeedMoreBytes           = 0,

            /// <summary>
            /// found a string but hit the end of the stream
            /// </summary>
            StringAtEndOfStream     = 1,

            /// <summary>
            /// a newline terminated the string
            /// </summary>
            StringTerminatedByCrLf  = 2,

            /// <summary>
            /// a character outside of the range of ascii characters
            /// was found, indicating that the BinaryReader likely
            /// contains binary, and not ascii, data
            /// </summary>
            NonAsciiCharacterFound  = 3
        };

        /// <summary>
        /// Peek into the stream until we find the next 0x13(CR) and 0x10(LF) characters,
        /// OR if we run into the end of the stream
        /// NeedMoreBytes - not enough bytes for a string, stream position is unchanged.
        /// StringAtEndOfStream - found string data but hit the end of the
        ///   stream before a crlf
        /// StringTerminatedByCrLf - found a CR LF, 0x0D 0x0A, terminated string
        /// </summary>
        /// <param name="br">
        /// A <see cref="BinaryReader"/>
        /// </param>
        /// <param name="s">
        /// A <see cref="System.String"/>
        /// </param>
        /// <returns>
        /// A <see cref="ReadNextLineResponses"/>
        /// </returns>
        public static ReadNextLineResponses ReadNextLineFromBinaryReader(BinaryReader br,
                                                                         out string s)
        {
#if PERFORMANCE_CRITICAL_LOGGING
            log.Debug("");
#endif

            const int crValue = 0x0d;
            const int lfValue = 0x0a;

            // TODO: not sure if this can be retrieved from .net internal class
            const int maxAsciiValue = 0x7f;

            s = null;

            Stream stream = br.BaseStream;
            long startingPosition = stream.Position;

            if(startingPosition == stream.Length)
            {
                return ReadNextLineResponses.NeedMoreBytes;
            }

            // search for the first 0x0D 0x0A pair, this will indicate the end of
            // the string
            int val = 0;
            int previousVal = 0;
            while(previousVal != -1)
            {
                previousVal = val; // save the previous val
                val = stream.ReadByte(); // read a new byte

                // if this value is outside of the range of ascii characters we should early out
                // because we may never find the string terminating characters in
                // a large binary stream
                if(val > maxAsciiValue)
                {
                    return ReadNextLineResponses.NonAsciiCharacterFound;
                }

                if((previousVal == crValue) &&
                   (val == lfValue))
                {
                    long stringLength = stream.Position - startingPosition - 2; // subtract 2 since we don't
                                                                                // want to include the 0x0D and 0x0A
                    stream.Position = startingPosition; // restore the original position
                    byte[] stringBytes = br.ReadBytes((int)stringLength); // read the string
                    br.ReadUInt16(); // read past the 0x0D and 0x0A

                    s = System.Text.ASCIIEncoding.ASCII.GetString(stringBytes);

                    return ReadNextLineResponses.StringTerminatedByCrLf;
                }
            }

            // if we read some bytes but not a lot of bytes then use what we got as a string
            long currentStreamPosition = stream.Position;
            if(currentStreamPosition != startingPosition)
            {
                long stringLength;

                // don't include an ending 0x0D if we found one and then hit the end of the stream
                if(previousVal == crValue)
                    stringLength = currentStreamPosition - startingPosition - 1;
                else
                    stringLength = currentStreamPosition - startingPosition;

                stream.Position = startingPosition; // restore the original position
                byte[] stringBytes = br.ReadBytes((int)stringLength); // read the string

                // ensure that we are at the end of the stream
                // necessary because we may leave an ending 0x0D around if we found one
                // and hit the end of the stream
                stream.Seek(0, SeekOrigin.End);

                s = System.Text.ASCIIEncoding.ASCII.GetString(stringBytes);

                return ReadNextLineResponses.StringAtEndOfStream;
            }

            // restore the original position
            stream.Position = startingPosition;

            return ReadNextLineResponses.NeedMoreBytes;
        }
    }
}
