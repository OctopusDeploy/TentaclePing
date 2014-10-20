﻿using System;
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
        private static readonly TimeSpan sendReceiveTimeout = TimeSpan.FromMinutes(2);

        static int Main(string[] args)
        {
            if (args.Length >= 1 && args.Length <= 2)
            {
                var hostname = args[0];
                var port = 10933;
                if (args.Length == 2)
                {
                    port = int.Parse(args[1]);
                }

                Console.WriteLine("Pinging " + hostname + " on port " + port);

                return ExecutePing(hostname, port);
            }
            PrintUsage();
            return 1;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("TentaclePing.exe <your-tentacle-hostname> [<port>]");
        }

        private static int ExecutePing(string hostname, int port)
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
                    Console.Write("Connect: ");
                    Console.ResetColor();

                    int bytesRead;
                    SendRequest(hostname, port, out bytesRead, out connected, out sslEstablished);

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

        private static void SendRequest(string hostname, int port, out int bytesRead, out bool connected, out bool sslEstablished)
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

                        ssl.AuthenticateAsClient(hostname, null, SslProtocols.Tls, false);

                        var writer = new StreamWriter(ssl);
                        writer.WriteLine("GET / HTTP/1.1");
                        writer.WriteLine("Host: " + hostname);
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
