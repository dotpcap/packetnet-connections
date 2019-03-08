/*
 * Copyright 2019 Chris Morgan <chmorgan@gmail.com>
 *
 * GPLv3 licensed, see LICENSE for full text
 * Commercial licensing available
 */
using System;

namespace PacketDotNet.Connections.Http
{
    /// <summary>
    /// Response to a http request
    /// </summary>
    public class HttpStatus : HttpMessage
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);        

        /// <summary>
        /// The request fo rwhich this status was generated
        /// </summary>
        public HttpRequest Request;

        /// <summary>
        /// copied from http://en.wikipedia.org/wiki/List_of_HTTP_status_codes
        /// </summary>
        public enum StatusCodes
        {
            /// <summary>
            /// 1xxx Informational
            /// </summary>
            /// <summary>
            /// server received request headers, client should send the request body
            /// </summary>
            Continue_100 = 100,

            /// <summary>
            /// Switching protocols
            /// </summary>
            SwitchingProtocols_101 = 101,

            /// <summary>
            /// WebDav related, see http://tools.ietf.org/html/rfc2518
            /// </summary>
            Processing_102 = 102,

            /// <summary>
            /// 2xx Success
            /// </summary>
            /// <summary>
            /// standard response for successful http requests
            /// </summary>
            OK_200 = 200,

            /// <summary>
            /// The request has been fulfilled and resulted in a new resource being created.
            /// </summary>
            Created_201 = 201,

            /// <summary>
            /// Request accepted but not processed
            /// </summary>
          Accepted_202 = 202,

            /// <summary>
            /// ??
            /// </summary>
            Non_authoritative_information_203 = 203,

            /// <summary>
            /// No content
            /// </summary>
          No_Content_204 = 204,

            /// <summary>
            /// Reset content
            /// </summary>
            Reset_Content_205 = 205,

            /// <summary>
            /// Partial content
            /// </summary>
            Partial_Content_206 = 206,

            /// <summary>
            /// Multi status
            /// </summary>
            Multi_Status_207 = 207,

            /// <summary>
            /// 3xx Redirection (client must take additional action to complete the request)
            /// </summary>
              Multiple_Choices_300 = 300,

            /// <summary>
            /// Moved
            /// </summary>
            Moved_Permanently_301 = 301,

            /// <summary>
            /// Found
            /// </summary>
            Found_302 = 302,

            /// <summary>
            /// See other
            /// </summary>
            See_Other_303 = 303,

            /// <summary>
            /// Not modified
            /// </summary>
            Not_Modified_304 = 304,

            /// <summary>
            ///  Use proxy
            /// </summary>
            Use_Proxy_305 = 305,

            /// <summary>
            /// Switch proxy
            /// </summary>
          Switch_Proxy_306 = 306,

            /// <summary>
            /// Temporary redirect
            /// </summary>
            Temporary_Redirect_307 = 307,

            /// <summary>
            /// Unknown
            /// </summary>
            Unknown = 99999 // we map all unknown values to this value
                  // until we have enum values for all of the status codes
        }

        /// <summary>
        /// This status code
        /// </summary>
        public StatusCodes StatusCode;

        /// <summary>
        /// This reason
        /// </summary>
        public string ReasonPhrase;

        /// <summary>
        /// The value of the 'Content-Type' field in the header
        /// </summary>
        public string ContentType
        {
            get
            {
                string key = "Content-Type";
                if(Headers.ContainsKey(key))
                {
                    return Headers[key];
                } else
                {
                    return String.Empty;
                }
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public HttpStatus()
        {
            log.Debug("");
        }

        /// <summary>
        /// Handles the first line of the status/response message
        /// </summary>
        /// <param name="line">
        /// A <see cref="System.String"/>
        /// </param>
        /// <returns>
        /// A <see cref="ProcessStatus"/>
        /// </returns>
        protected override ProcessStatus ProcessRequestResponseFirstLineHandler (string line)
        {
            log.DebugFormat("line is '{0}'", line);

            // format like: HTTP/1.1 200 OK
            // the first two parameters are split by spaces but the remaining text
            // is the 'reason phrase' and may contain spaces
            // so take care to split only the first two parameters on spaces
            char[] delimiters = new char[1];
            delimiters[0] = ' ';
            string[] tokens = line.Split(delimiters, 3);

            // do we have the correct number of tokens? if not
            // report the error to the higher level code
            int expectedTokensLength = 3;
            if(tokens.Length != expectedTokensLength)
            {
                log.DebugFormat("tokens.Length {0} != expectedTokensLength {1}, returning ProcessStatus.Error",
                                tokens.Length, expectedTokensLength);
                foreach(string s in tokens)
                {
                    log.Debug("token is " + s);
                }
                return ProcessStatus.Error;
            }

            // is the first token a valid method?
            if(!StringToHttpVersion(tokens[0], out HttpVersion))
            {
                log.DebugFormat("Unable to parse {0} into an http version, returning ProcessStatus.Error");
                return ProcessStatus.Error;
            }

            int statusCodeInt32;
            if(!Int32.TryParse(tokens[1], out statusCodeInt32))
            {
                throw new HttpStatusCodeParsingException("could not parse " + tokens[1] + " into an integer for a status code");
            } else
            {
                StatusCode = (StatusCodes)statusCodeInt32;
            }

            // the third token is the reason phrase
            ReasonPhrase = tokens[2];

            return ProcessStatus.Complete;
        }

        /// <summary>
        /// ToString override
        /// </summary>
        /// <returns>
        /// A <see cref="System.String"/>
        /// </returns>
        public override string ToString ()
        {
            return string.Format("StatusCode: {0} {1}",
                                 StatusCode, base.ToString());
        }
    }
}
