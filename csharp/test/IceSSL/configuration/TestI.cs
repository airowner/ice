//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

using System;
using System.Collections.Generic;
using ZeroC.Ice;
using Test;

namespace ZeroC.IceSSL.Test.Configuration
{
    internal sealed class SSLServer : IServer
    {
        internal SSLServer(Communicator communicator) => _communicator = communicator;

        public void
        noCert(Current current)
        {
            try
            {
                var tcpConnection = (TcpConnection)current.Connection!;
                TestHelper.Assert(tcpConnection.Endpoint.IsSecure);
                TestHelper.Assert(tcpConnection.Certificates == null);
            }
            catch (Exception)
            {
                TestHelper.Assert(false);
            }
        }

        public void
        checkCert(string subjectDN, string issuerDN, Current current)
        {
            try
            {
                var tcpConnection = (TcpConnection)current.Connection!;
                TestHelper.Assert(tcpConnection.Endpoint.IsSecure);
                TestHelper.Assert(tcpConnection.Certificates != null);
                TestHelper.Assert(tcpConnection.Certificates.Length == 2);
                TestHelper.Assert(tcpConnection.Certificates[0].Subject.Equals(subjectDN));
                TestHelper.Assert(tcpConnection.Certificates[0].Issuer.Equals(issuerDN));
            }
            catch (Exception)
            {
                TestHelper.Assert(false);
            }
        }

        public void
        checkCipher(string cipher, Current current)
        {
            try
            {
                var tcpConnection = (TcpConnection)current.Connection!;
                TestHelper.Assert(tcpConnection.Endpoint.IsSecure);
                TestHelper.Assert(tcpConnection.Cipher != null);
                TestHelper.Assert(tcpConnection.Cipher.Equals(cipher));
            }
            catch (Exception)
            {
                TestHelper.Assert(false);
            }
        }

        internal void Destroy() => _communicator.Destroy();

        private readonly Communicator _communicator;
    }

    internal sealed class ServerFactory : IServerFactory
    {
        public ServerFactory(string defaultDir) => _defaultDir = defaultDir;

        public IServerPrx createServer(Dictionary<string, string> props, Current current)
        {
            props["IceSSL.DefaultDir"] = _defaultDir;
            var communicator = new Communicator(props);
            ObjectAdapter adapter = communicator.CreateObjectAdapterWithEndpoints("ServerAdapter", "ssl");
            var server = new SSLServer(communicator);
            var prx = adapter.AddWithUUID(server, IServerPrx.Factory);
            _servers[prx.Identity] = server;
            adapter.Activate();
            return prx;
        }

        public void destroyServer(IServerPrx? srv, Current current)
        {
            if (_servers.TryGetValue(srv!.Identity, out SSLServer? server))
            {
                server.Destroy();
                _servers.Remove(srv.Identity);
            }
        }

        public void shutdown(Current current)
        {
            TestHelper.Assert(_servers.Count == 0);
            current.Adapter.Communicator.Shutdown();
        }

        private readonly string _defaultDir;
        private Dictionary<Identity, SSLServer> _servers = new Dictionary<Identity, SSLServer>();
    }
}
