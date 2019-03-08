/*
 * Copyright 2019 Chris Morgan <chmorgan@gmail.com>
 *
 * GPLv3 licensed, see LICENSE for full text
 * Commercial licensing available
 */
#define ExceptionLessConstructor

using System;
using System.IO;
using System.Collections.Generic;

namespace PacketDotNet.Connections.Http
{
    /// <summary>
    /// Class that watches the data provided by a TcpConnection
    /// and will perform callbacks when http messages are identified
    ///
    /// See http://en.wikipedia.org/wiki/HTTP
    ///
    /// Potential issue:
    ///
    /// Class can start monitoring an in progress session by starting
    /// monitoring on either a request OR a status message.
    ///
    /// Consider the case of a pipelined http session, we start monitoring
    /// at a status message but there are X requests outstanding
    ///
    /// - X pending requests
    /// - status A for the oldest request is sent from server to client
    /// - request Z for another image is sent from client to server
    /// - status B for second oldest request from server to client <----- this
    /// is the status we have trouble mapping because we don't have the 5
    /// pending requests
    ///
    /// So the error case is that if we use a simple queue for pending
    /// requests then we incorrectly map statusB to requestZ but it has to be
    /// the status for the second oldest request, one of the group of 5
    /// pending requests, because pipelining doesn't allow for reordering of
    /// commands.
    ///
    /// Can't think of a way to resolve this potential issue except for only allowing http
    /// sessions attached at the very beginning of a tcp session. This isn't easy to ensure
    /// but because it is the typical situation, ie. capturing is happening BEFORE most programs
    /// start up so we won't get into the issue of starting at a HttpStatus in almost all cases.
    ///
    /// Additionally, while we will mis-attribute some statuses to some requests if we end up getting
    /// all of the status messages then everything will re-synchronize itself.
    ///
    /// NOTE: HttpRequest and HttpStatus instances kept in pendingRequest and pendingStatus
    /// are not re-used, this allows the class to ensure that these instances are not modified
    /// after being passed to the user
    /// </summary>
    public class HttpSessionWatcher
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);        

        // NOTE: we have both a pending request and a status
        //       because http pipelining means that we can potentially
        //       see multiple requests before each response
        //       and that means a subsequent request could be
        //       being received at the same time as a previous
        //       request is responded to with a status
        private HttpRequest pendingRequest;
        private HttpStatus pendingStatus;

        /// <summary>
        /// Request was found delegate
        /// </summary>
        public delegate void OnHttpRequestFoundDelegate(HttpSessionWatcherRequestEventArgs e);

        /// <summary>
        /// On request found
        /// </summary>
        public OnHttpRequestFoundDelegate OnHttpRequestFound;

        /// <summary>
        /// Status found delegate
        /// </summary>
        public delegate void OnHttpStatusFoundDelegate(HttpSessionWatcherStatusEventArgs e);

        /// <summary>
        /// On status found
        /// </summary>
        public OnHttpStatusFoundDelegate OnHttpStatusFound;

        /// <summary>
        /// Error was found
        /// </summary>
        public delegate void OnHttpWatcherErrorDelegate(string error);

        /// <summary>
        /// On error found
        /// </summary>
        public OnHttpWatcherErrorDelegate OnHttpWatcherError;

        // keep track of outstanding requests waiting for responses
        private Queue<HttpRequest> requestsWaitingForStatus;

        /// <value>
        /// cache the IsDebugEnabled property to reduce the run-time performance hit
        /// of log4net statements in this class
        /// </value>
        private static bool IsDebugEnabled = log.IsDebugEnabled;

#if ExceptionLessConstructor
        private bool wasStarted = false;

        /// <summary>
        /// Whether a session was started
        /// </summary>
        public bool WasStarted
        {
            get { return wasStarted; }
            private set { wasStarted = value; }
        }
#endif

        private TcpStreamGenerator streamGenerator0;
        private TcpStreamGenerator streamGenerator1;

        private Dictionary<TcpStreamGenerator, MonitorTypes> tcpStreamGeneratorToMonitorType
            = new Dictionary<TcpStreamGenerator, MonitorTypes>();

        /// <summary>
        /// The type of monitor
        /// </summary>
        public enum MonitorTypes
        {
            /// <summary>
            /// Unknown
            /// </summary>
            Unknown,

            /// <summary>
            /// Client
            /// </summary>
            Client,

            /// <summary>
            /// Server
            /// </summary>
            Server
        }

        private void SetTypeForTcpStreamGenerator(TcpStreamGenerator sg,
                                                  MonitorTypes monitorType)
        {
            if(sg == streamGenerator0)
            {
                MonitorTypeFlow0 = monitorType;
            } else
            {
                MonitorTypeFlow1 = monitorType;
            }
        }

        /// <summary>
        /// Type of monitor for flow 0
        /// </summary>
        public MonitorTypes MonitorTypeFlow0
        {
            get
            {
                return tcpStreamGeneratorToMonitorType[streamGenerator0];
            }

            private set
            {
                if(value == MonitorTypeFlow0)
                {
                    return;
                }

                if(MonitorTypeFlow0 != HttpSessionWatcher.MonitorTypes.Unknown)
                {
                    throw new System.InvalidOperationException("setting montior type multiple times");
                } else
                {
                    tcpStreamGeneratorToMonitorType[streamGenerator0] = value;
                    tcpStreamGeneratorToMonitorType[streamGenerator1] = GetOtherMonitorType(value);
                }
            }
        }

        /// <summary>
        /// Based on a given monitor type determine what the other flow's monitor type
        /// is
        /// </summary>
        /// <param name="monitorType">
        /// A <see cref="MonitorTypes"/>
        /// </param>
        /// <returns>
        /// A <see cref="MonitorTypes"/>
        /// </returns>
        private static MonitorTypes GetOtherMonitorType(MonitorTypes monitorType)
        {
            if(monitorType  == MonitorTypes.Client)
            {
                return MonitorTypes.Server;
            } else if(monitorType == MonitorTypes.Server)
            {
                return MonitorTypes.Client;
            } else
            {
                return MonitorTypes.Unknown;
            }
        }

        /// <summary>
        /// Type of monitor for flow 1
        /// </summary>
        public MonitorTypes MonitorTypeFlow1
        {
            get
            {
                return tcpStreamGeneratorToMonitorType[streamGenerator1];
            }

            private set
            {
                if(value == MonitorTypeFlow1)
                {
                    return;
                }

                if(MonitorTypeFlow1 != HttpSessionWatcher.MonitorTypes.Unknown)
                {
                    throw new System.InvalidOperationException("setting montior type multiple times");
                } else
                {
                    tcpStreamGeneratorToMonitorType[streamGenerator1] = value;
                    tcpStreamGeneratorToMonitorType[streamGenerator0] = GetOtherMonitorType(value);
                }
            }
        }

        /// <summary>
        // NOTE: We only support starting a session monitor
        ///       if we catch a client request or server status
        ///       response at the head of the TcpStream.
        ///       We *could* pick things up in the middle but it would
        ///       require a bit more effort for what shouldn't be
        ///       a typical case.
        /// </summary>
        /// <param name="c">
        /// A <see cref="TcpConnection"/>
        /// </param>
        /// <param name="OnHttpRequestFound">
        /// A <see cref="OnHttpRequestFoundDelegate"/>
        /// </param>
        /// <param name="OnHttpStatusFound">
        /// A <see cref="OnHttpStatusFoundDelegate"/>
        /// </param>
        /// <param name="OnHttpWatcherError">
        /// A <see cref="OnHttpWatcherErrorDelegate"/>
        /// </param>
        public HttpSessionWatcher(TcpConnection c,
                                  OnHttpRequestFoundDelegate OnHttpRequestFound,
                                  OnHttpStatusFoundDelegate OnHttpStatusFound,
                                  OnHttpWatcherErrorDelegate OnHttpWatcherError)
        {
            if(IsDebugEnabled)
                log.Debug("");

            // attach stream generators to both flows of the given connection
            streamGenerator0 = new TcpStreamGenerator(c.Flows[0], new TimeSpan(0, 0, 60), null);
            streamGenerator0.OnCallback += HandleStreamGeneratorOnCallback;
            tcpStreamGeneratorToMonitorType[streamGenerator0] = MonitorTypes.Unknown;

            streamGenerator1 = new TcpStreamGenerator(c.Flows[1], new TimeSpan(0, 0, 60), null);
            streamGenerator1.OnCallback += HandleStreamGeneratorOnCallback;
            tcpStreamGeneratorToMonitorType[streamGenerator1] = MonitorTypes.Unknown;

            requestsWaitingForStatus = new Queue<HttpRequest>();

            // setup the delegates
            this.OnHttpRequestFound = OnHttpRequestFound;
            this.OnHttpStatusFound = OnHttpStatusFound;
            this.OnHttpWatcherError = OnHttpWatcherError;
        }

        TcpStreamGenerator.CallbackReturnValue HandleStreamGeneratorOnCallback (TcpStreamGenerator tcpStreamGenerator,
                                                                                TcpStreamGenerator.CallbackCondition condition)
        {
            switch(condition)
            {
            // stop monitoring if we have an error condition
            case TcpStreamGenerator.CallbackCondition.SizeLimitReached:
            case TcpStreamGenerator.CallbackCondition.OutOfRange:
            case TcpStreamGenerator.CallbackCondition.ConnectionTimeout:
            case TcpStreamGenerator.CallbackCondition.StreamError:
                string errorString = string.Format("condition == {0}, shutting down monitors",
                                                   condition);
                log.Warn(errorString);
                ShutDownMonitor(errorString);
                return TcpStreamGenerator.CallbackReturnValue.StopMonitoring;

            // early out if we don't have the next packet in sequence
            case TcpStreamGenerator.CallbackCondition.OutOfSequence:
                if(IsDebugEnabled)
                {
                    log.DebugFormat("condition {0} != TcpStreamMonitor.CallbackCondition.NextInSequence, returning ContinueMonitoring",
                                    condition);
                }
                return TcpStreamGenerator.CallbackReturnValue.ContinueMonitoring;

            case TcpStreamGenerator.CallbackCondition.DuplicateDropped:
                // nothing to do here, we dropped a duplicate entry
                return TcpStreamGenerator.CallbackReturnValue.ContinueMonitoring;

            // normal case, nothing to do here but fall through
            case TcpStreamGenerator.CallbackCondition.NextInSequence:
                break;

            default:
                string error = "Unknown condition of '" + condition + "'";
                throw new System.InvalidOperationException(error);
            }

            // process the data in the TcpStream until we encounter
            // an error/exception case
            HttpMessage.ProcessStatus processStatus;
            while(true)
            {
                processStatus = HandleTcpStreamGenerator(tcpStreamGenerator);

                if(IsDebugEnabled)
                    log.DebugFormat("processStatus is {0}", processStatus);

                // if an error was detected we should stop monitoring the monitor
                // with the error and delete the other monitor
                if(processStatus == HttpMessage.ProcessStatus.Error)
                {
                    var monitorType = tcpStreamGeneratorToMonitorType[tcpStreamGenerator];
                    string errorString = string.Format("Processing monitorType {0} got {1}, stopping monitor and deleting other monitor",
                                                       monitorType, processStatus);
                    ShutDownMonitor(errorString);
                    return TcpStreamGenerator.CallbackReturnValue.StopMonitoring;
                } else if(processStatus == HttpMessage.ProcessStatus.NeedMoreData)
                {
                    // not enough data remaining in the TcpStream so stop looping
                    // and ask the TcpStreamMonitor to continue monitoring
                    return TcpStreamGenerator.CallbackReturnValue.ContinueMonitoring;
                } else if(processStatus == HttpMessage.ProcessStatus.Continue)
                {
                    // just continue looping
                } else if(processStatus == HttpMessage.ProcessStatus.Continue)
                {
                    // just continue looping
                }
            }
        }

        /// <summary>
        /// Call the watcher error handler delegate if one has been assigned
        /// </summary>
        /// <param name="errorString">
        /// A <see cref="System.String"/>
        /// </param>
        private void ShutDownMonitor(string errorString)
        {
            // if we have a error handler, call it
            if(OnHttpWatcherError != null)
            {
                OnHttpWatcherError(errorString);
            }
        }

        // Process the given tcpStream until either
        // - ProcessStatus.Error OR
        // - ProcessStatus.NeedMoreData OR
        // - ProcessStatus.Completed
        //
        // ProcessStatus.Continue is hidden as this method will simply
        // loop internally, asking the current HttpMessage to continue processing
        //
        // see HttpMessage.Process() for return values from this method as they
        // are the same
        private HttpMessage.ProcessStatus HandleTcpStreamGenerator(TcpStreamGenerator tcpStreamGenerator)
        {
            var tcpStream = tcpStreamGenerator.tcpStream;
            var monitorType = tcpStreamGeneratorToMonitorType[tcpStreamGenerator];

            if(IsDebugEnabled)
                log.DebugFormat("monitorType: {0}", monitorType);

            HttpMessage theMessage = null;

            // retrieve the pending message or create and assign a new one if there
            // is no pending message
            if(monitorType == MonitorTypes.Client)
            {
                if(pendingRequest == null)
                {
                    if(IsDebugEnabled)
                        log.Debug("No pendingRequest, creating a new one");
                    pendingRequest = new HttpRequest();
                }

                theMessage = pendingRequest;
            } else if(monitorType == MonitorTypes.Server)
            {
                if(pendingStatus == null)
                {
                    if(IsDebugEnabled)
                        log.Debug("no pendingStatus, creating a new one");
                    pendingStatus = new HttpStatus();
                }

                theMessage = pendingStatus;
            }

            BinaryReader br = new BinaryReader(tcpStream);

            // default to NeedMoreData since that would be our state if
            // we didn't get into the while() loop due to running out of data
            HttpMessage.ProcessStatus status = HttpMessage.ProcessStatus.NeedMoreData;

            // outer loop runs while we have bytes to process
            //
            // NOTE: We can process until we run out of data because we re-use
            // the same message for the given TcpStream
            while(br.BaseStream.Position < br.BaseStream.Length)
            {
                // if we haven't identified the monitor type yet, attempt to do so now
                if(monitorType == HttpSessionWatcher.MonitorTypes.Unknown)
                {
                    // attempt to process as a request
                    // NOTE: Must assign pendingRequest BEFORE calling ProcessBinaryReader()
                    // which may pass the object if processing completes
                    pendingRequest = new HttpRequest();
                    status = ProcessBinaryReader(pendingRequest, MonitorTypes.Client, br);
                    if(status == HttpMessage.ProcessStatus.Error)
                    {
                        pendingRequest = null;

                        // attempt to process as a status
                        // NOTE: Must assign pendingStatus BEFORE calling ProcessBinaryReader()
                        // which may pass the object if processing completes
                        pendingStatus = new HttpStatus();
                        status = ProcessBinaryReader(pendingStatus, MonitorTypes.Server, br);
                        if(status == HttpMessage.ProcessStatus.Error)
                        {
                            pendingStatus = null;
                            return status;
                        } else // success
                        {
                            theMessage = pendingStatus;
                            monitorType = HttpSessionWatcher.MonitorTypes.Server;

                            // assign the monitor type
                            SetTypeForTcpStreamGenerator(tcpStreamGenerator, monitorType);
                        }
                    } else // success
                    {
                        theMessage = pendingRequest;
                        monitorType = HttpSessionWatcher.MonitorTypes.Client;

                        // assign the monitor type
                        SetTypeForTcpStreamGenerator(tcpStreamGenerator, monitorType);
                    }
                } else // otherwise just process the data like normal
                {
                    status = ProcessBinaryReader(theMessage, monitorType, br);
                }

                // stop processing if we need more data
                // or if we are complete since we are done with this message and
                // only by re-entering this function do we create a new one
                if((status == HttpMessage.ProcessStatus.NeedMoreData)
                   ||
                   (status == HttpMessage.ProcessStatus.Complete))
                {
                    break;
                }
            }

            return status;
        }

        private HttpMessage.ProcessStatus ProcessBinaryReader(HttpMessage theMessage,
                                                              MonitorTypes monitorType,
                                                              BinaryReader br)
        {
            HttpMessage.ProcessStatus status;

            status = theMessage.Process(br);

            if(IsDebugEnabled)
                log.DebugFormat("status == {0}", status);

            if(status == HttpMessage.ProcessStatus.Error)
            {
                log.Debug("ProcessStatus.Error");
            } else if(status == HttpMessage.ProcessStatus.Complete)
            {
                // send a completed notification to the appropriate handler
                if(monitorType == MonitorTypes.Client)
                {
                    if(OnHttpRequestFound != null)
                    {
                        if(IsDebugEnabled)
                            log.Debug("Calling OnHttpRequestFound() delegates");

                        try
                        {
                            OnHttpRequestFound(new HttpSessionWatcherRequestEventArgs(this, pendingRequest));
                        } catch(System.Exception)
                        {
                            // drop all exceptions thrown from handlers, its not our issue
                        }
                    }

                    // put this request into requests waiting for status queue
                    requestsWaitingForStatus.Enqueue(pendingRequest);

                    // clear out the pendingRequest since we finished with it
                    pendingRequest = null;
                } else if(monitorType == MonitorTypes.Server)
                {
                    // do we have any pending requests? if so we should
                    // dequeue one of them and assign it to the pendingStatus
                    // so the user can match the status with the request
                    if(requestsWaitingForStatus.Count != 0)
                        pendingStatus.Request = requestsWaitingForStatus.Dequeue();

                    if(OnHttpStatusFound != null)
                    {
                        if(IsDebugEnabled)
                            log.Debug("Calling OnHttpStatusFound() delegates");

                        try
                        {
                            OnHttpStatusFound(new HttpSessionWatcherStatusEventArgs(this, pendingStatus));
                        } catch(System.Exception)
                        {
                            // drop all exceptions thrown from handlers, its not our issue
                        }
                    }

                    // clear out the pendingStatus since we finished with it
                    pendingStatus = null;
                }

                // return completion
                status = HttpMessage.ProcessStatus.Complete;
            } else if(status == HttpMessage.ProcessStatus.NeedMoreData)
            {
                // need more data, return with our current position
            }

            return status;
        }
    }
}
