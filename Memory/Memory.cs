using LiveSplit.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace LiveSplit.VoxSplitter {
    public abstract class Memory : IDisposable {

        public Process game;
        protected string gameName;
        protected DateTime hookTime;

        public uint Tick { get; private set; } = 1;
        public void IncreaseTick() => ++Tick;

        public delegate void VersionEventHandler(object sender, string version);
        public VersionEventHandler SetVersion { get; set; }

        public Logger logger;

        protected Memory(Logger logger) => this.logger = logger;

        public virtual bool TryGetGameProcess() {
            if(DateTime.Now < hookTime) { return false; }

            hookTime = DateTime.Now.AddSeconds(1d);

            Process process = Process.GetProcesses().FirstOrDefault((Process p) =>
                p.ProcessName.StartsWith(gameName, StringComparison.OrdinalIgnoreCase) && !p.HasExited);

            if(process == null) {
                return false;
            }
            logger?.Log("Process Found: PID " + process.Id);
            game = process;
            return true;
        }

        public virtual bool IsReady() {
            if(!game?.HasExited ?? false) {
                return true;
            }
            if(TryGetGameProcess()) {
                OnGameHook();
            } else {
                return false;
            }
            return true;
        }

        protected virtual void OnGameHook() { }
        public virtual bool UpdateMemory(TimerModel timer) => true;
        public virtual bool Start(int start) => false;
        public virtual bool Split() => false;
        public virtual bool Reset(int reset) => false;
        public virtual bool Loading() => false;
        public virtual TimeSpan? GameTime() => null;
        public virtual void OnStart(TimerModel timer, HashSet<string> splits) { }
        public virtual void OnSplit(TimerModel timer) { }
        public virtual void OnReset(TimerModel timer) { }
        public virtual void Dispose() { }


        public class RemainingHashSet : HashSet<string> {
            protected Logger logger;

            public RemainingHashSet(Logger logger = null) {
                this.logger = logger;
            }

            public bool Split(string split) {
                logger?.Log("Try to split: " + split);
                if(Remove(split)) {
                    logger?.Log("Split: " + split);
                    return true;
                }
                return false;
            }
        }

        public class RemainingDictionary : Dictionary<string, HashSet<string>> {
            protected Logger logger;

            public RemainingDictionary(Logger logger = null) {
                this.logger = logger;
            }

            public void Setup(HashSet<string> splits) {
                Clear();

                foreach(string split in splits) {
                    int typeSeparator = split.IndexOf('_');

                    if(typeSeparator != -1) {
                        string type = split.Substring(0, typeSeparator);
                        if(!ContainsKey(type)) {
                            Add(type, new HashSet<string>());
                        }
                        string setting = split.Substring(typeSeparator + 1);
                        this[type].Add(setting);
                    } else {
                        Add(split, null);
                    }
                }
            }

            public bool Split(string type, string setting) {
                logger?.Log("Try to split setting: " + setting);
                if(this[type].Remove(setting)) {
                    if(this[type].Count == 0) {
                        Remove(type);
                    }
                    logger?.Log("Split setting: " + setting);
                    return true;
                }
                return false;
            }

            public bool Split(string type) {
                logger?.Log("Try to split type: " + type);
                if(Remove(type)) {
                    logger?.Log("Split type: " + type);
                    return true;
                }
                return false;
            }
        }
    }
}