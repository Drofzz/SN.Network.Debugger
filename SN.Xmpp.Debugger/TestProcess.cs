using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SN.Xmpp.Debugger
{
    class TestProcess
    {
        public async Task<TestResult> DoTestAsync(TestSource source, CancellationToken token, int i)
        {
            return await Task<TestResult>.Factory.StartNew((o) => DoTestInternal(source,token,i), token,TaskCreationOptions.LongRunning);
        }

        private TestResult DoTestInternal(TestSource source, CancellationToken token, int i, int retry = 0, int sleep = 0)
        {
            Thread.Sleep(sleep);
            if(retry> 5) return new TestResult(source,"",new TimeoutException("Tried To Connect 5 times"));
            var endPoint = source.EndPoint;
            var request = source.Request;
            var response = "";
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    socket.NoDelay = true;
                    socket.ReceiveTimeout = 30000;
                    socket.SendTimeout = 30000;
                    var connectResult = socket.BeginConnect(endPoint, null, null);
                    var success = connectResult.AsyncWaitHandle.WaitOne(new TimeSpan(0,0,10000), true);
                    if (!socket.Connected)
                    {
                        try
                        {
                            socket.Blocking = false;
                            socket.Send(new byte[1], 0, 0);
                        }
                        catch (SocketException e)
                        {
                            Console.WriteLine(e.Message);
                        }

                        return DoTestInternal(source, token, i, ++retry, 30);
                    }
                    using (var ns = new NetworkStream(socket))
                        {
                            Program.DebugMessage($"Test (#{i}) : {socket.LocalEndPoint} => {socket.RemoteEndPoint}");
                            using (var sw = new StreamWriter(ns, Encoding.Default, 4096, true))
                            {
                                var task = sw.WriteLineAsync(request);
                                if (!task.Wait(new TimeSpan(0, 0, 5)))
                                {
                                    Console.WriteLine("Failed Write");
                                }
                            }
                            using (var sr = new StreamReader(ns, Encoding.Default, true, 4096, true))
                            {
                                var task = sr.ReadLineAsync();
                                if (task.Wait(new TimeSpan(0, 0, 5)))
                                {
                                    response = task.Result;
                                }
                            }
                        }
                    }
                return source.CreateResult(response);
            }
            catch (Exception e)
            {
                Program.DebugMessage($"Test (#{i}) : {e.Message}");
                return source.CreateResult(response, e);
            }
        }
    }
}
