using System;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace TentaclePing
{
    static class Program
    {
        private static readonly TimeSpan sendReceiveTimeout = TimeSpan.FromMinutes(30);
        private static int chunkSizeInBytes;
        private static ProxySettings proxy;

        static int Main(string[] args)
        {
            // The proxy is configured via the HTTP_PROXY / HTTPS_PROXY environment
            // variables only; there is no command-line flag for it.
            try
            {
                proxy = ProxySettings.Resolve();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Invalid proxy setting: " + ex.Message);
                PrintUsage();
                return 1;
            }

            if (args.Length >= 1 && args.Length <= 5)
            {
                var hostname = args[0];
                var port = 10933;
                if (args.Length >= 2)
                {
                    port = int.Parse(args[1]);
                }
                var dataSize = 0;
                if (args.Length >= 3)
                {
                    dataSize = int.Parse(args[2]);
                }
                var chunkSize = 2;
                if (args.Length >= 4)
                {
                    chunkSize = int.Parse(args[3]);
                }

                #pragma warning disable SYSLIB0039
                var sslProtocol = SslProtocols.Tls;
                #pragma warning restore SYSLIB0039

                if (args.Length == 5)
                {
                    if (Enum.TryParse<SslProtocols>(args[4], out var parsedSslProtocol))
                    {
                        sslProtocol = parsedSslProtocol;
                    }
                }

                Console.WriteLine($"Using SSL Protocol: {sslProtocol}");
                if (proxy != null)
                {
                    Console.WriteLine($"Using HTTP proxy: {proxy.Host}:{proxy.Port}" + (proxy.HasCredentials ? " (with credentials)" : ""));
                }
                Console.WriteLine("Pinging " + hostname + " on port " + port + (dataSize == 0 ? "" : ", sending " + dataSize + "Mb of data in " + chunkSize + "Mb chunks"));
                chunkSizeInBytes = 1024*1024*chunkSize;
                return ExecutePing(hostname, port, dataSize, sslProtocol);
            }
            PrintUsage();
            return 1;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("TentaclePing.exe <your-tentacle-hostname> [<port>] [<datasize>] [<chunksize>] [<ssl_protocol>]");
            Console.WriteLine("A proxy can be set via the HTTP_PROXY or HTTPS_PROXY environment variable, e.g. http://[user:pass@]proxyhost:port.");
        }

        private static int ExecutePing(string hostname, int port, int dataSize, SslProtocols sslProtocol)
        {
            var failCount = 0;
            var successCount = 0;
            while (true)
            {
                var attempts = successCount + failCount;
                if (attempts > 0 && attempts % 10 == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("{0:n0}", successCount);
                    Console.ResetColor();
                    Console.Write(" successful connections, ");
                    Console.ForegroundColor = failCount == 0 ? ConsoleColor.White : ConsoleColor.Red;
                    Console.Write("{0:n0}", failCount);
                    Console.ResetColor();
                    Console.WriteLine(" failed connections. Hit Ctrl+C to quit any time.");
                }

                var start = Stopwatch.StartNew();
                var connected = false;
                var sslEstablished = false;
                var proxyTunnelEstablished = false;

                try
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write($"{DateTime.UtcNow:s} ");
                    Console.Write("Connect: ");
                    Console.ResetColor();

                    var bytesRead = 0;

                    if (dataSize > 0)
                    {
                        var dataToBeSent = 1024*1024*dataSize;

                        while (dataToBeSent > 0)
                        {
                            var data = new string('A', (dataToBeSent < chunkSizeInBytes ? dataToBeSent : chunkSizeInBytes));
                            SendRequest(hostname, port, sslProtocol, out bytesRead, out connected, out sslEstablished, out proxyTunnelEstablished, data);
                            dataToBeSent -= data.Length;
                        }
                    }
                    else
                    {
                        SendRequest(hostname, port, sslProtocol, out bytesRead, out connected, out sslEstablished, out proxyTunnelEstablished);
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Success! {0:n0}ms, {1:n0} bytes read", start.ElapsedMilliseconds, bytesRead);
                    Console.ResetColor();
                    successCount++;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    if (proxy != null)
                    {
                        Console.WriteLine("Failed! {0:n0}ms; proxy connected: {1}; tunnel: {2}; SSL: {3}", start.ElapsedMilliseconds, connected, proxyTunnelEstablished, sslEstablished);
                    }
                    else
                    {
                        Console.WriteLine("Failed! {0:n0}ms; connected: {1}; SSL: {2}", start.ElapsedMilliseconds, connected, sslEstablished);
                    }
                    Console.ResetColor();
                    Console.WriteLine(ex);
                    failCount++;
                }

                Thread.Sleep(500);
            }
        }

        private static void SendRequest(string hostname, int port, SslProtocols sslProtocol, out int bytesRead, out bool connected, out bool sslEstablished, out bool proxyTunnelEstablished, string data = null)
        {
            bytesRead = 0;
            connected = false;
            sslEstablished = false;
            proxyTunnelEstablished = false;

            using (var client = new TcpClient())
            {
                client.SendTimeout = (int) sendReceiveTimeout.TotalMilliseconds;
                client.ReceiveTimeout = (int) sendReceiveTimeout.TotalMilliseconds;

                // When a proxy is configured we connect to the proxy and ask it to
                // tunnel through to the Tentacle via HTTP CONNECT. This mirrors how the
                // Octopus Server reaches a Listening Tentacle through a proxy: the raw
                // TLS connection is tunnelled untouched (no SSL offloading).
                if (proxy != null)
                {
                    client.Connect(proxy.Host, proxy.Port);
                }
                else
                {
                    client.Connect(hostname, port);
                }

                using (var stream = client.GetStream())
                {
                    connected = true;

                    if (proxy != null)
                    {
                        EstablishProxyTunnel(stream, hostname, port);
                        proxyTunnelEstablished = true;
                    }

                    using (var ssl = new SslStream(stream, false, CertificateValidator, LocalCertificateSelection))
                    {
                        sslEstablished = true;

                        ssl.AuthenticateAsClient(hostname, null, sslProtocol, false);

                        var writer = new StreamWriter(ssl);
                        writer.WriteLine("GET / HTTP/1.1");
                        writer.WriteLine("Host: " + hostname);
                        writer.WriteLine(data);
                        writer.WriteLine();
                        writer.Flush();
                        using (var reader = new StreamReader(ssl))
                        {
                            bytesRead = reader.ReadToEnd().Length;
                        }
                    }
                }
            }
        }

        // Sends an HTTP CONNECT request to the proxy and verifies it agrees to tunnel
        // through to the target host. On success the stream is ready for the TLS handshake.
        private static void EstablishProxyTunnel(NetworkStream stream, string targetHost, int targetPort)
        {
            var request = new StringBuilder();
            request.Append($"CONNECT {targetHost}:{targetPort} HTTP/1.1\r\n");
            request.Append($"Host: {targetHost}:{targetPort}\r\n");
            if (proxy.HasCredentials)
            {
                var token = Convert.ToBase64String(Encoding.ASCII.GetBytes(proxy.Username + ":" + proxy.Password));
                request.Append($"Proxy-Authorization: Basic {token}\r\n");
            }
            request.Append("Proxy-Connection: Keep-Alive\r\n");
            request.Append("\r\n");

            var requestBytes = Encoding.ASCII.GetBytes(request.ToString());
            stream.Write(requestBytes, 0, requestBytes.Length);
            stream.Flush();

            var statusLine = ReadProxyStatusLine(stream);

            // Expected form: "HTTP/1.1 200 Connection established"
            var parts = statusLine.Split(new[] { ' ' }, 3);
            if (parts.Length < 2 || !int.TryParse(parts[1], out var statusCode))
            {
                throw new Exception("Unexpected response from proxy: " + statusLine);
            }
            if (statusCode != 200)
            {
                throw new Exception($"Proxy refused the CONNECT tunnel (HTTP {statusCode}): {statusLine}");
            }
        }

        // Reads the proxy's HTTP response headers a byte at a time, stopping at the
        // end of the header block so we don't consume any of the subsequent TLS bytes.
        private static string ReadProxyStatusLine(NetworkStream stream)
        {
            var response = new StringBuilder();
            while (!EndsWith(response, "\r\n\r\n"))
            {
                var b = stream.ReadByte();
                if (b == -1)
                {
                    throw new Exception("Proxy closed the connection before completing the CONNECT handshake.");
                }
                response.Append((char) b);
            }

            var headers = response.ToString();
            var firstLineEnd = headers.IndexOf("\r\n", StringComparison.Ordinal);
            return firstLineEnd >= 0 ? headers.Substring(0, firstLineEnd) : headers.Trim();
        }

        private static bool EndsWith(StringBuilder builder, string suffix)
        {
            if (builder.Length < suffix.Length)
            {
                return false;
            }
            var offset = builder.Length - suffix.Length;
            for (var i = 0; i < suffix.Length; i++)
            {
                if (builder[offset + i] != suffix[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static X509Certificate LocalCertificateSelection(object sender, string targethost, X509CertificateCollection localcertificates, X509Certificate remotecertificate, string[] acceptableissuers)
        {
            return null;
        }

        private static bool CertificateValidator(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslpolicyerrors)
        {
            return true;
        }

        // An HTTP proxy that the Tentacle connection should be tunnelled through, as
        // configured by the --proxy flag or the HTTP_PROXY / HTTPS_PROXY environment
        // variables. Only HTTP proxies with optional Basic authentication are supported,
        // matching what Octopus Server supports when reaching a Listening Tentacle.
        private class ProxySettings
        {
            public string Host { get; private set; }
            public int Port { get; private set; }
            public string Username { get; private set; }
            public string Password { get; private set; }
            public bool HasCredentials => !string.IsNullOrEmpty(Username);

            public static ProxySettings Resolve()
            {
                var value = Environment.GetEnvironmentVariable("HTTP_PROXY")
                            ?? Environment.GetEnvironmentVariable("http_proxy")
                            ?? Environment.GetEnvironmentVariable("HTTPS_PROXY")
                            ?? Environment.GetEnvironmentVariable("https_proxy");

                return string.IsNullOrEmpty(value) ? null : Parse(value);
            }

            private static ProxySettings Parse(string value)
            {
                // Accept values with or without a scheme, e.g.
                //   http://user:pass@host:8080   or   host:8080
                var normalized = value.Contains("://") ? value : "http://" + value;

                if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
                {
                    throw new Exception($"'{value}' is not a valid proxy URL. Expected http://[user:pass@]host:port");
                }

                var settings = new ProxySettings
                {
                    Host = uri.Host,
                    Port = uri.Port,
                };

                if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    var credentials = uri.UserInfo.Split(new[] { ':' }, 2);
                    settings.Username = Uri.UnescapeDataString(credentials[0]);
                    settings.Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "";
                }

                return settings;
            }
        }
    }
}
