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
    /// Session watcher request event arguments
    /// </summary>
    public class HttpSessionWatcherRequestEventArgs :EventArgs
    {
        private HttpSessionWatcher sessionWatcher;

        /// <summary>
        /// The session watcher
        /// </summary>
        public HttpSessionWatcher SessionWatcher
        {
            get { return sessionWatcher; }
        }

        private HttpRequest request;

        /// <summary>
        /// Request
        /// </summary>
        public HttpRequest Request
        {
            get { return request; }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="sessionWatcher">
        /// A <see cref="HttpSessionWatcher"/>
        /// </param>
        /// <param name="request">
        /// A <see cref="HttpRequest"/>
        /// </param>
        public HttpSessionWatcherRequestEventArgs(HttpSessionWatcher sessionWatcher,
                                                  HttpRequest request)
        {
            this.sessionWatcher = sessionWatcher;
            this.request = request;
        }
    }

    /// <summary>
    /// Session watcher status event arguments
    /// </summary>
    public class HttpSessionWatcherStatusEventArgs :EventArgs
    {
        private HttpSessionWatcher sessionWatcher;

        /// <summary>
        /// Session watcher
        /// </summary>
        public HttpSessionWatcher SessionWatcher
        {
            get { return sessionWatcher; }
        }

        private HttpStatus status;

        /// <summary>
        /// Status
        /// </summary>
        public HttpStatus Status
        {
            get { return status; }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="sessionWatcher">
        /// A <see cref="HttpSessionWatcher"/>
        /// </param>
        /// <param name="status">
        /// A <see cref="HttpStatus"/>
        /// </param>
        public HttpSessionWatcherStatusEventArgs(HttpSessionWatcher sessionWatcher,
                                                 HttpStatus status)
        {
            this.sessionWatcher = sessionWatcher;
            this.status = status;
        }
    }
}
