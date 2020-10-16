using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace LiveSplit.VoxSplitter {
    public class Logger {

#if !DEBUG
        private readonly string logFile;
        private const int LinesMax = 10000;
        private const int LinesErase = 500;

        private int lineNumber;
        private readonly Queue<string> linesQueue = new Queue<string>();
        private readonly object lockLines = new object();
        private readonly CancellationTokenSource tokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent manualEvent = new ManualResetEvent(false);
#endif
        private readonly Dictionary<string, Stopwatch> swList = new Dictionary<string, Stopwatch>();
        private readonly Dictionary<string, (int, double)> swAvg = new Dictionary<string, (int, double)>();

        public Logger(string logName) {
#if !DEBUG
            logFile = "_" + logName + ".log";
#endif
        }

        public void StartLogger() {
#if DEBUG
            NativeMethods.AllocConsole();
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
#else
            new Thread(() => {
                lineNumber = 0;
                if(!File.Exists(logFile)) {
                    File.Create(logFile);
                } else {
                    using(StreamReader reader = File.OpenText(logFile)) {
                        while(reader.ReadLine() != null) {
                            lineNumber++;
                        }
                    }
                }

                string line = null;
                while(true) {
                    manualEvent.WaitOne();
                    if(tokenSource.IsCancellationRequested) {
                        break;
                    }
                    lock(lockLines) {
                        if(linesQueue.Count != 0) {
                            line = linesQueue.Dequeue();
                        } else {
                            manualEvent.Reset();
                            continue;
                        }
                    }
                    WriteLine(line);
                    line = null;
                }
            }).Start();
#endif
        }

        public void StopLogger() {
#if DEBUG
            NativeMethods.FreeConsole();
#else
            tokenSource.Cancel();
            manualEvent.Set();
#endif
        }

        public void Log(string msg) {
            msg = DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg;
#if DEBUG
            Console.WriteLine(msg);
#else
            lock(lockLines) {
                linesQueue.Enqueue(msg);
                manualEvent.Set();
            }
#endif
        }

        public void WriteLine(string msg) {
#if !DEBUG
            if(lineNumber >= LinesMax) {
                string tempLog = logFile + "-temp";
                int linesSkipped = this.lineNumber - LinesMax + LinesErase;
                int lineNumber = 1;
                using(StreamReader reader = File.OpenText(logFile)) {
                    using(StreamWriter writer = File.CreateText(tempLog)) {
                        string line;
                        while((line = reader.ReadLine()) != null) {
                            if(lineNumber > linesSkipped) {
                                writer.WriteLine(line);
                            } else {
                                lineNumber++;
                            }
                        }
                    }
                }
                try {
                    File.Copy(tempLog, logFile, true);
                    this.lineNumber = LinesMax - LinesErase;
                } catch {
                    Options.Log.Error("Failed replacing log file");
                } finally {
                    try {
                        File.Delete(tempLog);
                    } catch {
                        Options.Log.Error("Failed deleting temp log file");
                    }
                }
            }

            try {
                using(StreamWriter writer = new StreamWriter(logFile, true)) {
                    writer.WriteLine(msg);
                    ++lineNumber;
                }
            } catch(Exception e) {
                Options.Log.Error(e.ToString());
            }
#endif
        }

        public void StartBenchmark(string key) {
            swList.Add(key, Stopwatch.StartNew());
        }

        public void StopBenchmark(string key, string prefix = "") {
            swList[key].Stop();
            Log(prefix + swList[key].Elapsed.ToString("mm:ss.fffffff"));
            swList.Remove(key);
        }

        public void StartAverageBenchmark(string key) {
            swList.Add(key, Stopwatch.StartNew());
            if(!swAvg.ContainsKey(key)) {
                swAvg.Add(key, (0, 0));
            }
        }

        public void StopAverageBenchmark(string key, string prefix = "") {
            swList[key].Stop();
            (int, double) tuple = swAvg[key];
            tuple.Item2 += swList[key].Elapsed.TotalMilliseconds;
            tuple.Item1++;
            swAvg[key] = tuple;
            Log(prefix + swList[key].Elapsed.ToString("mm:ss.fffffff") + " Average " + (tuple.Item2 / tuple.Item1));
            swList.Remove(key);
        }
    }
}