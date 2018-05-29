using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SN.Xmpp.Debugger
{
    class Program
    {
        private const bool IsDebug = true;
        private static Random Random = new Random();

        static IPAddress ConsoleReadAddress()
        {
            while (true)
            {
                Console.Write("Write IP: ");
                var input = Console.ReadLine();
                if (input != null && IPAddress.TryParse(input,out var address))
                {
                    return address;
                }
                else
                {
                    Console.WriteLine($"{input} is not a valid ip");
                }
            }
        }

        static int ConsoleReadNumber(string message)
        {
            while (true)
            {
                Console.Write(message);
                var input = Console.ReadLine();
                if (input != null && int.TryParse(input, out var number))
                {
                    return number;
                }
                else
                {
                    Console.WriteLine($"{input} was not a number");
                }
            }
        }

        static void Main(string[] args)
        {
            ConsoleKeyInfo cki = default(ConsoleKeyInfo);

            do
            {
                var ipaddress = ConsoleReadAddress();
                var port = ConsoleReadNumber("Port: ");
                var loop = ConsoleReadNumber("Runs: ");

                MainLoop(ipaddress,port,loop);

                Console.WriteLine("Done.");
                cki = Console.ReadKey();
            } while (cki.Key != ConsoleKey.Escape);
        }

        private static void MainLoop(IPAddress address, int port, int number)
        {
            using (var source = new CancellationTokenSource())
            {
                var sources = new List<TestSource>();

                for (var i = 0; i < number; i++)
                {
                    sources.Add(new TestSource(new IPEndPoint(address, port), GenerateRandomString(Random.Next(0, 4096))));
                }

                var task = Run(sources, source.Token);

                try
                {
                    task.Wait(source.Token);
                }
                catch (OperationCanceledException e)
                {
                    Console.WriteLine("Operation was cancelled");
                }


                var result = task.Result;

                var testResults = result;
                var exceptions = testResults.Where(r => r.Exception != null).ToList();
                var success = testResults.Where(r => r.Response == r.Source.Request && BitConverter.ToString(r.Hash) == BitConverter.ToString(r.Source.Hash)).ToList();
                var fail = testResults.Except(success);

                Console.WriteLine("Results: ");
                Console.WriteLine($"Process Time : {result.ProcessTime.TotalMilliseconds}ms");
                Console.WriteLine($"Success: {success.Count()}");
                Console.WriteLine($"Fails: {fail.Count()}");

                Console.WriteLine($"Exceptions: {exceptions.Count()}");
                foreach (var r1 in exceptions.GroupBy(r => r
                        .Exception.GetType())
                    .Select(r => (r.Key.Name, r.Count()))
                )
                {
                    Console.WriteLine($"Exceptions {r1.Item1}: {r1.Item2}");
                }
            }
        }

        static IEnumerable<TestSource> GenerateSources(params TestSource[] sources)
        {
            return new List<TestSource>(sources);
        }

        static async Task<TestCollection> Run(IEnumerable<TestSource> sources, CancellationToken token)
        {
            return await Task<TestCollection>.Factory.StartNew(() => DoBatchTest(sources,token), token);
        }

        static TestCollection DoBatchTest(IEnumerable<TestSource> sources, CancellationToken token)
        {
            int i = 1;
            return sources.Select(source => new TestProcess().DoTestAsync(source,token,i++)).ToTestCollection();
        }

        static string GenerateRandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[Random.Next(s.Length)]).ToArray());
        }

        public static void DebugMessage(string message)
        {
            if (IsDebug)
            {
                Console.WriteLine(message);
            }
        }
    }

    static class TestExtensions
    {
        public static TestCollection ToTestCollection(this IEnumerable<Task<TestResult>> resultTasks)
        {
            var tasksList = resultTasks.ToList();
            var collection = new TestCollection();
            Parallel.ForEach(tasksList, (task, state, arg3) =>
            {
                lock (collection)
                {
                    collection.Add(task.Result);
                }
            });
            return collection;
        }
    }

    class TestSource
    {
        public static MD5 MD5 = MD5.Create();
        public EndPoint EndPoint { get; }
        public string Request { get; }
        public int ByteLenght => Encoding.Default.GetByteCount(Request);
        public byte[] Hash => TestSource.MD5.ComputeHash(Encoding.Default.GetBytes(Request));

        public TestResult CreateResult(string response, Exception exception = default(Exception))
        {
            return new TestResult(this,response,exception);
        }

        public TestSource(EndPoint endPoint, string request)
        {
            EndPoint = endPoint;
            Request = request;
        }
    }

    class TestResult
    {

        public string Response { get; }
        public int ByteLenght => Encoding.Default.GetByteCount(Response);
        public byte[] Hash => TestSource.MD5.ComputeHash(Encoding.Default.GetBytes(Response));
        public TestSource Source { get; }
        public Exception Exception { get; }

        public TestResult(TestSource source, string response, Exception exception)
        {
            Source = source;
            Response = response;
            Exception = exception;
        }
    }

    class TestCollection : ICollection<TestResult>
    {
        private readonly IList<TestResult> _results = new List<TestResult>();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private bool _finish = false;

        public TimeSpan ProcessTime => _stopwatch.Elapsed;

        public int Count => _results.Count;

        public bool IsReadOnly => _finish;

        public void Add(TestResult item)
        {
            if (!_finish)
            {
                if (!_stopwatch.IsRunning) _stopwatch.Start();
                _results.Add(item);
            }
            else
            {
                throw new ReadOnlyException("Collection is Finalized");
            }
        }

        public Task Add(Task<TestResult> task)
        {
            return task.ContinueWith(task1 =>
            {
                lock (_results)
                {
                    Add(task1.Result);
                }
            });
        }

        public void Finish()
        {
            if(!_stopwatch.IsRunning) _stopwatch.Stop();
            _finish = true;
        }

        public void Clear()
        {
            if (!_finish)
            {
                _results.Clear();
            }
            else
            {
                throw new ReadOnlyException("Collection is Finalized");
            }
        }

        public bool Contains(TestResult item)
        {
            return _results.Contains(item);
        }

        public void CopyTo(TestResult[] array, int arrayIndex)
        {
            _results.CopyTo(array,arrayIndex);
        }

        public IEnumerator<TestResult> GetEnumerator()
        {
            return _results.GetEnumerator();
        }

        public bool Remove(TestResult item)
        {
            if (!_finish)
            {
                return _results.Remove(item);
            }
            else
            {
                throw new ReadOnlyException("Collection is Finalized");
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _results.GetEnumerator();
        }
    }
}
