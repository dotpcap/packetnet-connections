/*
 * Copyright 2019 Chris Morgan <chmorgan@gmail.com>
 *
 * GPLv3 licensed, see LICENSE for full text
 * Commercial licensing available
 */
using log4net;
using PacketDotNet;
using PacketDotNet.Connections;
using PacketDotNet.Connections.Http;
using SharpPcap;
using SharpPcap.LibPcap;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace Test
{
    public class HttpSessionWatcherTests
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private class ExpectedMessageContainer
        {
            public enum MessageTypes
            {
                HttpRequest,
                HttpStatus
            }

            public HttpMessage message;
            public MessageTypes messageType;
        }

        private List<ExpectedMessageContainer> expectedMessages;
        private int expectedMessageIndex;

        private void OnHttpRequestFound(HttpSessionWatcherRequestEventArgs e)
        {
            log.Debug("");
            log.Debug(e.Request.ToString());

            CheckMessage(e.Request, ExpectedMessageContainer.MessageTypes.HttpRequest);
        }

        private void OnHttpStatusFound(HttpSessionWatcherStatusEventArgs e)
        {
            log.Debug("");
            log.Debug(e.Status.ToString());

            CheckMessage(e.Status, ExpectedMessageContainer.MessageTypes.HttpStatus);
        }

        private void OnHttpWatcherError(string error)
        {
            log.Debug("");
            log.Debug(error);
        }

        private void CheckMessage(HttpMessage httpMessage, ExpectedMessageContainer.MessageTypes theMessageType)
        {
            try
            {
                log.DebugFormat("expectedMessageIndex {0}", expectedMessageIndex);

                if (expectedMessageIndex > expectedMessages.Count)
                {
                    throw new System.InvalidOperationException("expectedMessageIndex "
                                                               + expectedMessageIndex
                                                               + " > expectedMessages.Count"
                                                               + expectedMessages.Count);
                }

                ExpectedMessageContainer expectedMessage = expectedMessages[expectedMessageIndex];

                Assert.True(expectedMessage.messageType == theMessageType, "Message type does not match");

                // perform comparisons that are generic to all HttpMessages
                Assert.Equal(expectedMessage.message.HttpVersion, httpMessage.HttpVersion);

                // if we expected a zero length body then we expect that
                // the actual message has a null body since it was never set
                if (expectedMessage.message.Body.Length == 0)
                {
                    Assert.True(httpMessage.Body == null);
                }
                else
                {
                    Assert.Equal(expectedMessage.message.Body.Length, httpMessage.Body.Length);
                }

                if (theMessageType == ExpectedMessageContainer.MessageTypes.HttpRequest)
                {
                    HttpRequest expected = (HttpRequest)expectedMessage.message;
                    HttpRequest request = (HttpRequest)httpMessage;

                    Assert.Equal(expected.Url, request.Url);
                    Assert.Equal(expected.Method, request.Method);
                }
                else
                {
                    HttpStatus expected = (HttpStatus)expectedMessage.message;
                    HttpStatus status = (HttpStatus)httpMessage;

                    Assert.Equal(expected.StatusCode, status.StatusCode);
                }

                // move to the next message
                expectedMessageIndex++;

                log.Debug("message matched");
            }
            catch (System.Exception e)
            {
                // catch exceptions because the HttpSessionMonitor code will drop them

                log.Error("caught exception", e);
                Assert.True(false);
            }
        }

        private void ProcessPackets(ICaptureDevice dev,
                                    TcpConnectionManager tcpConnectionManager)
        {
            // reset the expected message index at the start of the test run
            expectedMessageIndex = 0;

            GetPacketStatus status;
            PacketCapture e;
            while ((status = dev.GetNextPacket(out e)) == GetPacketStatus.PacketRead)
            {
                var rawCapture = e.GetPacket();
                var p = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);

                var tcpPacket = p.Extract<TcpPacket>();

                // skip non-tcp packets, http is a tcp based protocol
                if (p == null)
                {
                    continue;
                }

                log.Debug("passing packet to TcpConnectionManager");
                tcpConnectionManager.ProcessPacket(rawCapture.Timeval,
                                                   tcpPacket);
            }

            // did we get all of the messages?
            Assert.Equal(expectedMessages.Count, expectedMessageIndex);
        }

#if true
        [Fact]
        public void TestHttpPostSession()
        {
            log4net.Config.XmlConfigurator.Configure(LogManager.GetRepository(Assembly.GetCallingAssembly()));

            string captureFilename = "../../../captureFiles/http_post_gziped_contents.pcap";
            var tcpConnectionManager = new TcpConnectionManager();
            tcpConnectionManager.OnConnectionFound += HandleTcpConnectionManagerOnConnectionFound;

            // open the offline file
            var dev = new CaptureFileReaderDevice(captureFilename);
            dev.Open();
            Assert.True(dev != null, "failed to open " + captureFilename);

            // setup the expected events
            ExpectedMessageContainer expectedMessage;
            HttpRequest request;
            HttpStatus status;
            expectedMessages = new List<ExpectedMessageContainer>();

            // POST /ajax/chat/typ.php
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = ExpectedMessageContainer.MessageTypes.HttpRequest;
            request = new HttpRequest();
            request.Url = "/ajax/chat/typ.php";
            request.Method = HttpRequest.Methods.Post;
            request.HttpVersion = HttpMessage.HttpVersions.Http11;
            request.Body = new byte[242];
            expectedMessage.message = request;
            expectedMessages.Add(expectedMessage);

            // HTTP 1.1 OK
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = ExpectedMessageContainer.MessageTypes.HttpStatus;
            status = new HttpStatus();
            status.StatusCode = HttpStatus.StatusCodes.OK_200;
            status.HttpVersion = HttpMessage.HttpVersions.Http11;
            status.Body = new byte[418];
            expectedMessage.message = status;
            expectedMessages.Add(expectedMessage);

            // POST /ajax/chat/send.php
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = ExpectedMessageContainer.MessageTypes.HttpRequest;
            request = new HttpRequest();
            request.Url = "/ajax/chat/send.php";
            request.Method = HttpRequest.Methods.Post;
            request.HttpVersion = HttpMessage.HttpVersions.Http11;
            request.Body = new byte[390];
            expectedMessage.message = request;
            expectedMessages.Add(expectedMessage);

            // HTTP 1.1 OK
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = ExpectedMessageContainer.MessageTypes.HttpStatus;
            status = new HttpStatus();
            status.StatusCode = HttpStatus.StatusCodes.OK_200;
            status.HttpVersion = HttpMessage.HttpVersions.Http11;
            status.Body = new byte[1132];
            expectedMessage.message = status;
            expectedMessages.Add(expectedMessage);

            ProcessPackets(dev, tcpConnectionManager);
        }

        void HandleTcpConnectionManagerOnConnectionFound(TcpConnection c)
        {
            var httpSessionWatcher = new HttpSessionWatcher(c,
                                                            OnHttpRequestFound,
                                                            OnHttpStatusFound,
                                                            OnHttpWatcherError);
        }
#endif

#if true

        [Fact]
        public void TestHttpGetSession()
        {
            log4net.Config.XmlConfigurator.Configure(LogManager.GetRepository(Assembly.GetCallingAssembly()));

            string captureFilename = "../../../captureFiles/http_get_request_and_response_brotli_compressed.pcap";
            var tcpConnectionManager = new TcpConnectionManager();
            tcpConnectionManager.OnConnectionFound += HandleTcpConnectionManagerOnConnectionFound;

            // open the offline file
            var dev = new CaptureFileReaderDevice(captureFilename);
            dev.Open();
            Assert.True(dev != null, "failed to open " + captureFilename);

            // setup the expected events
            ExpectedMessageContainer expectedMessage;
            HttpRequest request;
            HttpStatus status;
            expectedMessages = new List<ExpectedMessageContainer>();

            // GET /api/areaInformation/?requestAction=GetMapMunicipalityList
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = ExpectedMessageContainer.MessageTypes.HttpRequest;
            request = new HttpRequest();
            request.Url = "/api/areaInformation/?requestAction=GetMapMunicipalityList";
            request.Method = HttpRequest.Methods.Get;
            request.HttpVersion = HttpMessage.HttpVersions.Http11;
            request.Body = new byte[0];
            expectedMessage.message = request;
            expectedMessages.Add(expectedMessage);

            // HTTP 1.1 OK
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = ExpectedMessageContainer.MessageTypes.HttpStatus;
            status = new HttpStatus();
            status.StatusCode = HttpStatus.StatusCodes.OK_200;
            status.HttpVersion = HttpMessage.HttpVersions.Http11;
            status.Body = new byte[8269];
            expectedMessage.message = status;
            expectedMessages.Add(expectedMessage);

            ProcessPackets(dev, tcpConnectionManager);
        }

#endif

#if false
        [Fact]
        public void TestHttpGetWithGzippedContent()
        {
            throw new System.NotImplementedException();
        }

        [Fact]
        public void TestHttpCorruptedSession()
        {
            throw new System.NotImplementedException();
        }
#endif

#if true
        [Fact]
        public void TestHttpPipelining()
        {
            log4net.Config.XmlConfigurator.Configure(LogManager.GetRepository(Assembly.GetCallingAssembly()));

            string captureFilename = "../../../captureFiles/http_pipelined_session.pcap";
            var tcpConnectionManager = new TcpConnectionManager();
            tcpConnectionManager.OnConnectionFound += HandleTcpConnectionManagerOnConnectionFound;

            // open the offline file
            var dev = new CaptureFileReaderDevice(captureFilename);
            dev.Open();
            Assert.True(dev != null, "failed to open " + captureFilename);

            // setup the expected events
            ExpectedMessageContainer expectedMessage;
            HttpRequest request;
            HttpStatus status;
            expectedMessages = new List<ExpectedMessageContainer>();

            // GET /
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = HttpSessionWatcherTests.ExpectedMessageContainer.MessageTypes.HttpRequest;
            request = new HttpRequest();
            request.Url = "/";
            request.Method = HttpRequest.Methods.Get;
            request.HttpVersion = HttpMessage.HttpVersions.Http11;
            request.Body = new byte[0];
            expectedMessage.message = request;
            expectedMessages.Add(expectedMessage);

            // HTTP 1.1 OK
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = HttpSessionWatcherTests.ExpectedMessageContainer.MessageTypes.HttpStatus;
            status = new HttpStatus();
            status.StatusCode = HttpStatus.StatusCodes.OK_200;
            status.HttpVersion = HttpMessage.HttpVersions.Http11;
            status.Body = new byte[60985];
            expectedMessage.message = status;
            expectedMessages.Add(expectedMessage);

            // GET /dynjs/loader/DynJs2e30b68dfa7a63636c3ef66d7875e2f7
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = HttpSessionWatcherTests.ExpectedMessageContainer.MessageTypes.HttpRequest;
            request = new HttpRequest();
            request.Url = "/dynjs/loader/DynJs2e30b68dfa7a63636c3ef66d7875e2f7";
            request.Method = HttpRequest.Methods.Get;
            request.HttpVersion = HttpMessage.HttpVersions.Http11;
            request.Body = new byte[0];
            expectedMessage.message = request;
            expectedMessages.Add(expectedMessage);

            // HTTP 1.1 OK
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = HttpSessionWatcherTests.ExpectedMessageContainer.MessageTypes.HttpStatus;
            status = new HttpStatus();
            status.StatusCode = HttpStatus.StatusCodes.OK_200;
            status.HttpVersion = HttpMessage.HttpVersions.Http11;
            status.Body = new byte[1123];
            expectedMessage.message = status;
            expectedMessages.Add(expectedMessage);

            // GET /img/menu-secondary.gif
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = HttpSessionWatcherTests.ExpectedMessageContainer.MessageTypes.HttpRequest;
            request = new HttpRequest();
            request.Url = "/img/menu-secondary.gif";
            request.Method = HttpRequest.Methods.Get;
            request.HttpVersion = HttpMessage.HttpVersions.Http11;
            request.Body = new byte[0];
            expectedMessage.message = request;
            expectedMessages.Add(expectedMessage);

            // HTTP 1.1 OK
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = HttpSessionWatcherTests.ExpectedMessageContainer.MessageTypes.HttpStatus;
            status = new HttpStatus();
            status.StatusCode = HttpStatus.StatusCodes.OK_200;
            status.HttpVersion = HttpMessage.HttpVersions.Http11;
            status.Body = new byte[227];
            expectedMessage.message = status;
            expectedMessages.Add(expectedMessage);


            // here are the pipelined requests

            // GET /environment/Future_Battlegrounds_for_Habitat_Conservation/s.jpg
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = HttpSessionWatcherTests.ExpectedMessageContainer.MessageTypes.HttpRequest;
            request = new HttpRequest();
            request.Url = "/environment/Future_Battlegrounds_for_Habitat_Conservation/s.jpg";
            request.Method = HttpRequest.Methods.Get;
            request.HttpVersion = HttpMessage.HttpVersions.Http11;
            request.Body = new byte[0];
            expectedMessage.message = request;
            expectedMessages.Add(expectedMessage);

            // GET /users/smitas/s.png
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = HttpSessionWatcherTests.ExpectedMessageContainer.MessageTypes.HttpRequest;
            request = new HttpRequest();
            request.Url = "/users/smitas/s.png";
            request.Method = HttpRequest.Methods.Get;
            request.HttpVersion = HttpMessage.HttpVersions.Http11;
            request.Body = new byte[0];
            expectedMessage.message = request;
            expectedMessages.Add(expectedMessage);

            // GET /users/Bukowsky/s.png
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = HttpSessionWatcherTests.ExpectedMessageContainer.MessageTypes.HttpRequest;
            request = new HttpRequest();
            request.Url = "/users/Bukowsky/s.png";
            request.Method = HttpRequest.Methods.Get;
            request.HttpVersion = HttpMessage.HttpVersions.Http11;
            request.Body = new byte[0];
            expectedMessage.message = request;
            expectedMessages.Add(expectedMessage);

            // GET /users/d2002/s.png
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = HttpSessionWatcherTests.ExpectedMessageContainer.MessageTypes.HttpRequest;
            request = new HttpRequest();
            request.Url = "/users/d2002/s.png";
            request.Method = HttpRequest.Methods.Get;
            request.HttpVersion = HttpMessage.HttpVersions.Http11;
            request.Body = new byte[0];
            expectedMessage.message = request;
            expectedMessages.Add(expectedMessage);

            // GET /img/tab-line.gif
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = HttpSessionWatcherTests.ExpectedMessageContainer.MessageTypes.HttpRequest;
            request = new HttpRequest();
            request.Url = "/img/tab-line.gif";
            request.Method = HttpRequest.Methods.Get;
            request.HttpVersion = HttpMessage.HttpVersions.Http11;
            request.Body = new byte[0];
            expectedMessage.message = request;
            expectedMessages.Add(expectedMessage);

            // GET /img/admin-dialogg.gif
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = HttpSessionWatcherTests.ExpectedMessageContainer.MessageTypes.HttpRequest;
            request = new HttpRequest();
            request.Url = "/img/admin-dialogg.gif";
            request.Method = HttpRequest.Methods.Get;
            request.HttpVersion = HttpMessage.HttpVersions.Http11;
            request.Body = new byte[0];
            expectedMessage.message = request;
            expectedMessages.Add(expectedMessage);

            // HTTP 1.1 OK
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = HttpSessionWatcherTests.ExpectedMessageContainer.MessageTypes.HttpStatus;
            status = new HttpStatus();
            status.StatusCode = HttpStatus.StatusCodes.OK_200;
            status.HttpVersion = HttpMessage.HttpVersions.Http11;
            status.Body = new byte[1300];
            expectedMessage.message = status;
            expectedMessages.Add(expectedMessage);

            // GET /pets_animals/Maradona_of_the_Jungle/t.jpg
            expectedMessage = new ExpectedMessageContainer();
            expectedMessage.messageType = HttpSessionWatcherTests.ExpectedMessageContainer.MessageTypes.HttpRequest;
            request = new HttpRequest();
            request.Url = "/pets_animals/Maradona_of_the_Jungle/t.jpg";
            request.Method = HttpRequest.Methods.Get;
            request.HttpVersion = HttpMessage.HttpVersions.Http11;
            request.Body = new byte[0];
            expectedMessage.message = request;
            expectedMessages.Add(expectedMessage);

            ProcessPackets(dev, tcpConnectionManager);
        }
#endif
    }
}
