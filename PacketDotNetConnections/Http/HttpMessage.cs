/*
 * Copyright 2019 Chris Morgan <chmorgan@gmail.com>
 *
 * GPLv3 licensed, see LICENSE for full text
 * Commercial licensing available
 */
using System;
using System.Collections.Generic;
using System.IO;

namespace PacketDotNet.Connections.Http
{
    /// <summary>
    /// Base class of either HttpRequest or HttpStatus, contains common
    /// methods and data structures used in HTTP message parsing
    /// </summary>
    public class HttpMessage
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);        

        /// <summary>
        /// Key is the header field name, the key is the value
        /// </summary>
        public Dictionary<string, string> Headers = new Dictionary<string, string>();

        /// <summary>
        /// the uncompressed version of the body, after Process() returns
        /// ProcessStatus.Complete
        /// </summary>
        public byte[] Body;

        /// <summary>
        /// the compressed version of the body if there is a body, after Process()
        /// returns ProcessStatus.Complete
        /// </summary>
        public byte[] CompressedBody;

        /// <summary>
        /// used when decoding http messages with "Transfer-Encoding: chunked"
        /// </summary>
        private int ChunkLength;

        /// <summary>
        /// Possible http protocol versions
        /// </summary>
        public enum HttpVersions
        {
            /// <summary>
            /// HTTP 1.0
            /// </summary>
            Http10,

            /// <summary>
            /// HTTP 1.1
            /// </summary>
            Http11
        }

        /// <summary>
        /// Http version for this message
        /// </summary>
        public HttpVersions HttpVersion;

        /// <value>
        /// Cached contentLength value to avoid retrieving
        /// the value multiple times
        /// </value>
        private int? cachedContentLength;

        /// <summary>
        /// returns the content length if one is present in the
        /// headers or -1 if none are present
        /// </summary>
        public int ContentLength
        {
            get
            {
                // if we have a value, return it here
                if(cachedContentLength.HasValue)
                    return cachedContentLength.Value;

                if(Headers.ContainsKey("Content-Length"))
                {
                    int contentLength;
                    if(!Int32.TryParse(Headers["Content-Length"], out contentLength))
                    {
                        throw new HttpContentLengthParsingException("unable to parse " + Headers["Content-Length"] + " into a valid content length");
                    }

                    // cache the value
                    cachedContentLength = contentLength;

                    return contentLength;
                } else
                {
                    return -1;
                }
            }
        }

        /// <summary>
        /// Types of content encodings
        /// </summary>
        public enum ContentEncodings
        {
            /// <summary>
            /// No encoding
            /// </summary>
            None,

            /// <summary>
            /// Gzipped content
            /// </summary>
            Gzip,

            /// <summary>
            /// Deflated content
            /// </summary>
            Deflate
        }

        /// <summary>
        /// Content encoding for this message
        /// </summary>
        public ContentEncodings ContentEncoding
        {
            get
            {
                if(Headers.ContainsKey("Content-Encoding"))
                {
                    string encodingType = Headers["Content-Encoding"];
                    if(encodingType == "gzip")
                    {
                        return ContentEncodings.Gzip;
                    }
                    else if(encodingType == "deflate")
                    {
                        return ContentEncodings.Deflate;
                    } else
                    {
                        throw new System.InvalidOperationException("unknown Content-Encoding of "
                                                                   + encodingType);
                    }
                } else
                {
                    return ContentEncodings.None;
                }
            }
        }

        /// <summary>
        /// Transfer encoding field from the http header
        /// </summary>
        public string TransferEncoding
        {
            get
            {
                if(Headers.ContainsKey("Transfer-Encoding"))
                {
                    return Headers["Transfer-Encoding"];
                } else
                {
                    return String.Empty;
                }
            }
        }

        /// <summary>
        /// Http message parsing state
        /// </summary>
        public enum ProcessStatus
        {
            /// <summary>
            /// Similar to Complete but typically used for sub-processing steps
            /// </summary>
            Continue,

            /// <summary>
            /// More data is necessary to complete parsing
            /// </summary>
            NeedMoreData,

            /// <summary>
            /// Parsing complete, a message has been decoded
            /// </summary>
            Complete,

            /// <summary>
            /// Parsing failed due to an error condition such as malformed or unexpected data
            /// </summary>
            Error
        }

        /// <summary>
        /// Represents the phases of an http message
        /// </summary>
        public enum Phases
        {
            /// <summary>
            /// The first state, identifies the http message as a request or status(response)
            /// </summary>
            RequestResponse,

            /// <summary>
            /// Headers, several header lines, header section is termined by a /r/n
            /// </summary>
            Headers,

            /// <summary>
            /// Contents of the message, the body
            /// body is in one piece (ContentLength is != -1, indicating that it is set)
            /// </summary>
            Body,

            /// <summary>
            /// Body is encoded via chunked mode
            /// body is separated into chunks, length in ascii followed by /r/n
            /// </summary>
            BodyChunkedLength,

            /// <summary>
            /// The data portion of the chunk, also followed by /r/n
            /// </summary>
            BodyChunkData,

            /// <summary>
            /// The separator between data chunks
            /// </summary>
            BodyChunkSeparator
        }

        private Phases phase;

        /// <summary>
        /// The current parsing phase
        /// </summary>
        public Phases Phase
        {
            get { return phase; }
        }

        // cache the debug enabled property
        private static bool IsDebugEnabled = log.IsDebugEnabled;

        /// <summary>
        /// BinaryReader.BaseStream position is preserved in cases other than Continue
        /// </summary>
        /// <param name="br">
        /// A <see cref="BinaryReader"/>
        /// </param>
        /// <returns>
        /// A <see cref="ProcessStatus"/>
        /// ProcessStatus.Error if any errors are detected, the stream position
        ///         is restored to the starting position
        /// ProcessStatus.NeedMoreData if we run into a situation where there isn't
        ///         enough data to continue processing. Stream position is at the position
        ///         where the internal state machine is waiting to resume when additional
        ///         data arrives
        /// </returns>
        public ProcessStatus Process (BinaryReader br)
        {
            if(IsDebugEnabled)
                log.Debug("");

            string line;
            HttpMessage.ProcessStatus processStatus;

            while(true)
            {
                // the request response and header phases work in terms of lines,
                // terminated by /r/n, this is different from the body phase that
                // is a blob of bytes if "Content-Length" was set in the headers, or
                // chunked mode if "Transfer-Encoding" was set in the headers
                if((Phase == Phases.RequestResponse) ||
                   (Phase == Phases.Headers) ||
                   (Phase == Phases.BodyChunkedLength) ||
                   (Phase == Phases.BodyChunkSeparator))
                {
                    if(IsDebugEnabled)
                        log.Debug("Line reading phase");

                    // save the starting position so we can restore if we don't have
                    // enough bytes
                    long startingPosition = br.BaseStream.Position;

                    // read the next line from the binary reader
                    var readNextLine
                        = BinaryReaderHelper.ReadNextLineFromBinaryReader(br, out line);

                    if(IsDebugEnabled)
                    {
                        log.DebugFormat("readNextLine {0}, line of '{1}'",
                                        readNextLine, line);
                    }

                    // NOTE: Because we ensure that we have a string that was properly terminated by
                    //       crlf we can consider 'Needs`MoreData' cases inside of this if() to be
                    //       errors
                    if(readNextLine == BinaryReaderHelper.ReadNextLineResponses.StringTerminatedByCrLf)
                    {
                        if(Phase == Phases.RequestResponse)
                        {
                            if(IsDebugEnabled)
                                log.Debug("Phase == Phases.RequestResponse");

                            processStatus = ProcessRequestResponseFirstLineHandler(line);

                            if(processStatus == ProcessStatus.Error)
                            {
                                log.Debug("ProcessStatus.Error() from ProcessRequestResponseFirstLineHandler()");

                                // rewind to the starting position
                                br.BaseStream.Position = startingPosition;

                                return ProcessStatus.Error;
                            } else if(processStatus == ProcessStatus.Complete)
                            {
                                log.Debug("status == ProcessStatus.Compete, moving to Phases.Headers");

                                // done with the request/response first line handler
                                // move to the next phase
                                phase = Phases.Headers;
                            } else
                            {
                                // make sure to catch any unexpected status return values
                                throw new System.InvalidOperationException("Unexpected processStatus of " + processStatus + " returned by ProcessRequestResponseFirstLineHandler()");
                            }
                        } else if(Phase == Phases.Headers)
                        {
                            if(IsDebugEnabled)
                                log.Debug("Phase == Phases.Headers");

                            processStatus = ProcessHeaders(line);

                            if(processStatus == ProcessStatus.Error)
                            {
                                log.Debug("ProcessStatus.Error from ProcessHeaders(), moving back to startingPosition and returning ProcessStatus.Error");

                                // rewind to the starting position
                                br.BaseStream.Position = startingPosition;

                                return ProcessStatus.Error;
                            } else if(processStatus == ProcessStatus.Complete)
                            {
                                if(IsDebugEnabled)
                                    log.Debug("Done with headers");

                                // done with headers
                                // if we have a content length then move to Phases.Body
                                // otherwise we are done with the request
                                if(ContentLength != -1)
                                {
                                    if(IsDebugEnabled)
                                    {
                                        log.DebugFormat("ContentLength is {0}, moving to Body phase",
                                                        ContentLength);
                                    }

                                    phase = Phases.Body;
                                } else if(TransferEncoding == "chunked")
                                {
                                    if(IsDebugEnabled)
                                        log.Debug("TransferEncoding chunked, moving to Phases.BodyChunkedLength");
                                    phase = Phases.BodyChunkedLength;
                                } else
                                {
                                    // we are all done, no data follows the headers
                                    if(IsDebugEnabled)
                                        log.Debug("returning ProcessStatus.Complete");
                                    return ProcessStatus.Complete;
                                }
                            } else if(processStatus == ProcessStatus.Continue)
                            {
                                if(IsDebugEnabled)
                                    log.Debug("ProcessHeaders() returned 'Continue'");
                            } else
                            {
                                // make sure to catch any unexpected status return values
                                throw new System.InvalidOperationException("Unexpected processStatus of " + processStatus + " returned by ProcessHeaders()");
                            }
                        } else if(Phase == Phases.BodyChunkedLength)
                        {
                            if(IsDebugEnabled)
                                log.Debug("Phase == Phases.BodyChunkedLength");

                            processStatus = PhaseBodyChunkLength(line);
                            if(processStatus != ProcessStatus.Continue)
                            {
                                return processStatus;
                            }                    
                        } else if(Phase == Phases.BodyChunkSeparator)
                        {
                            if(IsDebugEnabled)
                                log.Debug("Phase == Phases.BodyChunkSeparator");

                            // we expect the line to be empty
                            if(line != String.Empty)
                            {
                                log.Error("Expected a line that was String.Empty in the BodyChunkSeparator state but instead got '"
                                          + line + "'");
                                return ProcessStatus.Error;
                            }

                            // if the ChunkLength is 0 then we are all done with the chunked data
                            if(ChunkLength == 0)
                            {
                                log.Debug("Found ChunkLength of zero, decoding content and returning ProcessStatus.Complete");
                
                                // we have the entire body, decode the content
                                DecodeContent();
                
                                return ProcessStatus.Complete;
                            } else // we have more chunks pending
                            {
                                // go back to the BodyChunkedLength phase
                                if(IsDebugEnabled)
                                    log.Debug("Going back to Phases.BodyChunkedLength");
                                phase = Phases.BodyChunkedLength;
                            }
                        }
                    } else if(readNextLine == BinaryReaderHelper.ReadNextLineResponses.NonAsciiCharacterFound)
                    {
                        if(IsDebugEnabled)
                        {
                            log.DebugFormat("found a non-ascii character, this appears to be a binary stream, returning an error code");
                        }

                        return ProcessStatus.Error;
                    } else if((readNextLine == BinaryReaderHelper.ReadNextLineResponses.StringAtEndOfStream) ||
                              (readNextLine == BinaryReaderHelper.ReadNextLineResponses.NeedMoreBytes))
                    {
                        if(IsDebugEnabled)
                        {
                            log.DebugFormat("restoring the starting position and returning ProcessStatus.NeedMoreData",
                                      readNextLine);
                        }

                        // restore the starting position
                        br.BaseStream.Position = startingPosition;

                        // if we don't have enough data or if we have data but no crlf
                        // then report this
                        return ProcessStatus.NeedMoreData;
                    } else
                    {
                        // Treat errors more seriously in DEBUG mode
#if DEBUG
                        throw new System.InvalidOperationException("unknown readNextLine value of " + readNextLine + ", returning an error");
#else
                        log.Error("unknown readNextLine value of " + readNextLine + ", returning an error");
                        return ProcessStatus.Error;
#endif
                    }
                } else if(Phase == Phases.Body)
                {
                    if(IsDebugEnabled)
                        log.Debug("Phase == Phases.Body");

                    processStatus = PhaseBody(br);
                    if(processStatus != ProcessStatus.Continue)
                    {
                        return processStatus;
                    }
                } else if(Phase == Phases.BodyChunkData)
                {
                    if(IsDebugEnabled)
                        log.Debug("Phase == Phases.BodyChunkData");

                    processStatus = PhaseBodyChunkData(br);
                    if(processStatus != ProcessStatus.Continue)
                    {
                        return processStatus;
                    }                    
                } else
                {
                    throw new System.InvalidCastException("Unknown phase of " + Phase);
                }
            } // while(true)
        }

        private ProcessStatus PhaseBody(BinaryReader br)
        {
            if(IsDebugEnabled)
                log.Debug("");

            if(ContentLength == -1)
            {
                throw new System.InvalidOperationException("ContentLength of -1 but we are in the Body phase");
            }

            long bytesInStream = br.BaseStream.Length - br.BaseStream.Position;

            // do we have all of the bytes we need for the body of this
            // http message?
            if(ContentLength <= bytesInStream)
            {
                // read them into the body
                Body = br.ReadBytes(ContentLength);

                if(IsDebugEnabled)
                    log.Debug("Read Body, decoding content and returning ProcessStatus.Complete");

                // we have the entire body, decode the content
                DecodeContent();

                // indicate that we are complete
                return ProcessStatus.Complete;
            } else
            {
                if(IsDebugEnabled)
                {
                    log.DebugFormat("ContentLength is {0} but we only have {1} bytes in the stream, returning {2}",
                                    ContentLength, bytesInStream, ProcessStatus.NeedMoreData);
                }
                return ProcessStatus.NeedMoreData;
            }
        }

        private ProcessStatus PhaseBodyChunkLength(string line)
        {
            if(IsDebugEnabled)
                log.Debug("");

            // NOTE: take care to convert the value from hex format
            try
            {
                // NOTE: there can be spaces after the chunk length ascii
                //       and these mess up Convert.ToInt32() so remove them here
                line = line.Trim();

                ChunkLength = Convert.ToInt32(line, 16);
            } catch(System.Exception e)
            {
                throw new HttpChunkLengthParsingException("unable to parse '" + line + "' into a chunk length",
                                                          e);
            }

            if(IsDebugEnabled)
            {
                log.Debug("ChunkLength is " + ChunkLength);

                log.Debug("Moving to Phases.BodyChunkData");
            }

            phase = Phases.BodyChunkData;

            return ProcessStatus.Continue;
        }
        
        private ProcessStatus PhaseBodyChunkData(BinaryReader br)
        {
            if(IsDebugEnabled)
                log.Debug("");

            // if our ChunkLength is 0 then this is the chunk that ends a series
            // of chunks, so we can simply go to the next phase
            if(ChunkLength == 0)
            {
                phase = Phases.BodyChunkSeparator;
                return ProcessStatus.Continue;
            }

            long bytesInStream = br.BaseStream.Length - br.BaseStream.Position;

            // do we have all of the bytes we need for this particular chunk?
            if(ChunkLength <= bytesInStream)
            {
                // read this chunk
                byte[] chunk = br.ReadBytes(ChunkLength);

                // if we have no existing Body, use this chunk
                if(Body == null)
                {
                    Body = chunk;
                } else
                {
                    byte[] newBuffer = new byte[Body.Length + chunk.Length];
                    Array.Copy(Body, newBuffer, Body.Length);
                    Array.Copy(chunk, 0, newBuffer, Body.Length, chunk.Length);
                    Body = newBuffer;
                }

                // move to the final phase for this chunk, the /r/n separator
                // that goes between chunks
                if(IsDebugEnabled)
                    log.Debug("Moving to Phases.BodyChunkSeparator");
                phase = Phases.BodyChunkSeparator;

                return ProcessStatus.Continue;
            } else
            {
                if(IsDebugEnabled)
                {
                    log.DebugFormat("ChunkLength is {0} but we only have {1} bytes in the stream, returning {2}",
                                    ChunkLength, bytesInStream, ProcessStatus.NeedMoreData);
                }
                return ProcessStatus.NeedMoreData;
            }
        }

        /// <summary>
        /// Http header handling routine common to requests and status responses
        /// </summary>
        /// <param name="line">
        /// A <see cref="System.String"/>
        /// </param>
        /// <returns>
        /// A <see cref="ProcessStatus"/>
        /// </returns>
        protected ProcessStatus ProcessHeaders(string line)
        {
            // the end of the headers is indicated by
            // an empty line, "\r\n" only but the \r\n is
            // stripped off by this point
            if(line == String.Empty)
                return ProcessStatus.Complete;

            // header lines are in the format of:
            // "Header Name: Value\r\n" (the \r\n are stripped from the line at this point)

            string[] delimiters = new string[1];
            delimiters[0] = ": ";
            string[] tokens = line.Split(delimiters, 2, StringSplitOptions.None);

            // check for the proper number of tokens
            int expectedTokensLength = 2;
            if(tokens.Length != expectedTokensLength)
            {
                string errorString = String.Empty;
                for(int i = 0; i < tokens.Length; i++)
                {
                    errorString += String.Format("tokens[{0}] = {1} ",
                                                 i, tokens[i]);
                }

                log.WarnFormat("tokens.Length was {0} and not {1}, tokens are {2}",
                               tokens.Length, expectedTokensLength,
                               errorString);

                return ProcessStatus.Error;
            }

            if(IsDebugEnabled)
            {
                log.DebugFormat("tokens[0] '{0}', tokens[1] '{1}'",
                                tokens[0], tokens[1]);
            }

            // the dictionary key is the header, the key is the value
            Headers[tokens[0]] = tokens[1];

            return ProcessStatus.Continue;
        }

        /// <summary>
        /// Must be implemented by a specific type of this class
        /// </summary>
        /// <param name="line">
        /// A <see cref="System.String"/>
        /// </param>
        /// <returns>
        /// A <see cref="ProcessStatus"/>
        /// </returns>
        protected virtual ProcessStatus ProcessRequestResponseFirstLineHandler(string line)
        {
            throw new System.NotImplementedException("each http message type must implement method");
        }

        /// <summary>
        /// Override of ToString()
        /// </summary>
        /// <returns>
        /// A <see cref="System.String"/>
        /// </returns>
        public override string ToString ()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            sb.AppendLine(string.Format("[HttpMessage: HttpVersion={0}, ContentLength={1}, ContentEncoding={2}]",
                                        HttpVersion,
                                        ContentLength,
                                        ContentEncoding));

            foreach(KeyValuePair<string, string> kvp in Headers)
            {
                sb.AppendLine(String.Format("'{0}' '{1}'", kvp.Key, kvp.Value));
            }

            if(Body != null)
            {
                sb.AppendLine("Body.Length " + Body.Length);
                sb.AppendLine("Body:\n" + System.Text.ASCIIEncoding.ASCII.GetString(Body));
            }

            if(CompressedBody != null)
            {
                sb.AppendLine("CompressedBody.Length " + CompressedBody.Length);
            }

            return sb.ToString();
        }

        /// <summary>
        /// returns true if the string was parsed, false if the string wasn't valid
        /// </summary>
        /// <param name="httpVersionString">
        /// A <see cref="System.String"/>
        /// </param>
        /// <param name="httpVersion">
        /// A <see cref="HttpVersions"/>
        /// </param>
        /// <returns>
        /// A <see cref="System.Boolean"/>
        /// </returns>
        public static bool StringToHttpVersion(string httpVersionString,
                                               out HttpVersions httpVersion)
        {
            httpVersion = HttpVersions.Http10;

            // format is like "HTTP/1.1", so version comes after the slash
            string[] tokens = httpVersionString.Split('/');

            if(tokens[0] != "HTTP")
            {
                log.Info("Expected 'HTTP' got " + tokens[0]);                
                return false;
            }

            if(tokens[1] == "1.0")
            {
                httpVersion = HttpVersions.Http10;
            }
            else if(tokens[1] == "1.1")
            {
                httpVersion = HttpVersions.Http11;
            }
            else
            {
                throw new HttpVersionParsingException("unable to parse " + httpVersionString + " into a valid version");
            }

            // we were able to parse the version
            return true;
        }

        private void DecodeContent()
        {
            if(ContentEncoding == ContentEncodings.None)
            {
                return;
            }

            System.IO.Stream s;
            if(ContentEncoding == ContentEncodings.Deflate)
            {
                s = new ICSharpCode.SharpZipLib.Zip.Compression.Streams.InflaterInputStream(
                                        new System.IO.MemoryStream(Body),
                                        new ICSharpCode.SharpZipLib.Zip.Compression.Inflater(true));
            } else if(ContentEncoding == ContentEncodings.Gzip)
            {
                s = new ICSharpCode.SharpZipLib.GZip.GZipInputStream(new System.IO.MemoryStream(Body));
            } else
            {
                throw new System.NotImplementedException("Unknown ContentEncoding of " + ContentEncoding);
            }

            System.IO.MemoryStream ms = new System.IO.MemoryStream();
            int readChunkSize = 2048;

            byte[] uncompressedChunk = new byte[readChunkSize];
            int sizeRead;
            do
            {
                sizeRead = s.Read(uncompressedChunk, 0, readChunkSize);
                if(sizeRead > 0)
                    ms.Write(uncompressedChunk, 0, sizeRead);
                else
                    break; // break out of the while loop
            } while(sizeRead > 0);

            s.Close();

            // copy the CompressedBody to CompressedBody
            CompressedBody = Body;

            // extract the compressed body over the existing body
            Body = ms.ToArray();
        }
    }
}
