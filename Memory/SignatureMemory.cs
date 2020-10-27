using LiveSplit.ComponentUtil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LiveSplit.VoxSplitter {
    public abstract class SignatureMemory : Memory {

        protected readonly ScannableData scanData;
        protected CancellationTokenSource tokenSource;
        protected CancellationToken token;
        protected Task scanTask;

        protected SignatureMemory(Logger logger) : base(logger) {
            scanData = new ScannableData();
        }

        public override bool TryGetGameProcess() {
            if(base.TryGetGameProcess()) {
                RestartScan();
                return true;
            }
            return false;
        }

        protected void RestartScan() {
            if(!scanTask?.IsCompleted ?? false) {
                tokenSource.Cancel();
                scanTask.Wait();
            }
            tokenSource = new CancellationTokenSource();
            token = tokenSource.Token;
            scanTask = Task.Factory.StartNew(() => {
                try {
                    scanData.ResetPointers();
                    ScanMemory();
                    OnScanDone();
                    logger.Log("Scan task terminated");
                } catch {
                    logger.Log("Scan task aborted");
                }
            });
        }

        public override bool IsReady() => base.IsReady() && scanTask.IsCompleted;

        protected virtual void OnScanDone() { }

        protected virtual void ScanMemory() {
            logger.Log("Scanning memory");
            while(true) {
                token.ThrowIfCancellationRequested();

                foreach(KeyValuePair<string, Dictionary<string, SignatureHolder>> moduleScan in scanData) {
                    token.ThrowIfCancellationRequested();

                    if(String.IsNullOrEmpty(moduleScan.Key)) {
                        foreach(MemoryBasicInformation page in game.MemoryPages(scanData.AllPages)) {
                            token.ThrowIfCancellationRequested();
                            SearchAllSigs(moduleScan.Value, new SignatureScanner(game, page.BaseAddress, (int)page.RegionSize));
                            if(scanData.AllSignaturesFound) {
                                break;
                            }
                        }
                    } else {
                        ProcessModuleWow64Safe module = game.Modules().FirstOrDefault(m => m.ModuleName == moduleScan.Key);
                        if(module == null) {
                            continue;
                        }
                        SearchAllSigs(moduleScan.Value, new SignatureScanner(game, module.BaseAddress, module.ModuleMemorySize));
                    }
                }

                token.ThrowIfCancellationRequested();

                if(!scanData.AllSignaturesFound) {
                    Thread.Sleep(2000);
                    continue;
                }

                logger.Log("Done scanning");
                break;
            }
        }

        protected virtual void SearchAllSigs(Dictionary<string, SignatureHolder> scanData, SignatureScanner scanner) {
            foreach(KeyValuePair<string, SignatureHolder> kvp in scanData) {
                token.ThrowIfCancellationRequested();

                SignatureHolder sig = kvp.Value;
                if(sig.Found || !sig.DoScan) {
                    continue;
                }

                if(sig.ValueTarget == null) {
                    foreach(VersionScan vScan in sig.Scans) {
                        token.ThrowIfCancellationRequested();

                        if((sig.Pointer = scanner.Scan(vScan)) != default) {
                            sig.Verion = vScan.Version;
                            string verString = sig.Scans.Length > 1 ? " with " + vScan.Version + " version" : "";
                            logger.Log(kvp.Key + " Found : " + sig.Pointer.ToString("X") + verString);
                        }
                    }
                } else {
                    Type targetType = sig.ValueTarget?.target.GetType();
                    foreach(VersionScan vScan in sig.Scans) {
                        token.ThrowIfCancellationRequested();

                        foreach(IntPtr ptr in scanner.ScanAll(vScan)) {
                            token.ThrowIfCancellationRequested();

                            IntPtr resPtr = game.DerefOffsets(EDerefType.Auto, game.Read<IntPtr>(ptr), sig.ValueTarget?.offsets);
                            object res;
                            if(targetType == typeof(string)) {
                                res = game.ReadString(resPtr, EStringType.Auto);
                            } else {
                                object[] args = new object[] { game, resPtr, Activator.CreateInstance(targetType) };
                                typeof(ExtensionMethods).GetMethods().First(m => m.Name == "Read")
                                                        .MakeGenericMethod(targetType)
                                                        .Invoke(game, args);
                                res = args[2];
                            }

                            if(!res.Equals(sig.ValueTarget?.target)) {
                                continue;
                            }

                            sig.Pointer = ptr;
                            sig.Verion = vScan.Version;
                            string verString = sig.Scans.Length > 1 ? " with " + vScan.Version + " version" : "";
                            logger.Log(kvp.Key + " Found : " + sig.Pointer.ToString("X") + verString);
                            break;
                        }
                    }
                }
            }
        }

        public override void Dispose() => tokenSource.Cancel();
    }

    public class ScannableData : Dictionary<string, Dictionary<string, SignatureHolder>> {
        public bool AllPages { get; set; } = false;

        public bool AllSignaturesFound => this.All(m => m.Value.All(kvp => kvp.Value.Found));

        public void ResetPointers() {
            foreach(Dictionary<string, SignatureHolder> mod in Values) {
                foreach(SignatureHolder sig in mod.Values) {
                    sig.Pointer = default;
                }
            }
        }
    }

    public class SignatureHolder {
        public SignatureHolder(int offset, string sig) {
            Scans = new VersionScan[] { new VersionScan(offset, sig) };
        }

        public SignatureHolder(params VersionScan[] signatures) {
            Scans = signatures.ToArray();
        }

        public VersionScan[] Scans { get; }
        public string Verion { get; set; } = String.Empty;
        public IntPtr Pointer { get; set; } = default;
        public bool DoScan { get; set; } = true;
        public (object target, int[] offsets)? ValueTarget { get; set; } = null;

        public bool Found => Pointer != default || !DoScan;
    }

    public class VersionScan : SigScanTarget {
        public VersionScan(int offset, string sig) : this(null, offset, sig) { }
        public VersionScan(string version, int offset, string sig) : base(offset, sig) {
            Version = version;
        }

        public string Version { get; set; }
    }
}