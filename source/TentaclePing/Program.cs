using System;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace TentaclePing
{
    static class Program
    {
        private static readonly TimeSpan sendReceiveTimeout = TimeSpan.FromMinutes(30);
        private static int chunkSizeInBytes;

        static int Main(string[] args)
        {
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
                var sslProtocol = SslProtocols.Tls;
                if (args.Length == 5)
                {
                    if (Enum.TryParse<SslProtocols>(args[4], out var parsedSslProtocol))
                    {
                        sslProtocol = parsedSslProtocol;
                    }
                }

                Console.WriteLine($"Using SSL Protocol: {sslProtocol}");
                Console.WriteLine("Pinging " + hostname + " on port " + port + (dataSize == 0 ? "" : ", sending " + dataSize + "Mb of data in " + chunkSize + "Mb chunks"));
                chunkSizeInBytes = 1024*1024*chunkSize;
                return ExecutePing(hostname, port, dataSize, sslProtocol);
            }
            PrintUsage();
            return 1;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("TentaclePing.exe <your-tentacle-hostname> [<port>] [<datasize>] [<chunksize>]");
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
                            SendRequest(hostname, port, sslProtocol, out bytesRead, out connected, out sslEstablished, data);
                            dataToBeSent -= data.Length;
                        }
                    }
                    else
                    {
                        SendRequest(hostname, port, sslProtocol, out bytesRead, out connected, out sslEstablished);
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Success! {0:n0}ms, {1:n0} bytes read", start.ElapsedMilliseconds, bytesRead);
                    Console.ResetColor();
                    successCount++;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Failed! {0:n0}ms; connected: {1}; SSL: {2}", start.ElapsedMilliseconds, connected, sslEstablished);
                    Console.ResetColor();
                    Console.WriteLine(ex);
                    failCount++;
                }

                Thread.Sleep(500);
            }
        }

        private static void SendRequest(string hostname, int port, SslProtocols sslProtocol, out int bytesRead, out bool connected, out bool sslEstablished, string data = null)
        {
            using (var client = new TcpClient())
            {
                client.SendTimeout = (int) sendReceiveTimeout.TotalMilliseconds;
                client.ReceiveTimeout = (int) sendReceiveTimeout.TotalMilliseconds;
                client.Connect(hostname, port);

                using (var stream = client.GetStream())
                {
                    connected = true;

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

        private static X509Certificate LocalCertificateSelection(object sender, string targethost, X509CertificateCollection localcertificates, X509Certificate remotecertificate, string[] acceptableissuers)
        {
            return null;
        }

        private static bool CertificateValidator(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslpolicyerrors)
        {
            return true;
        }
    }
}
