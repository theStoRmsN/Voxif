using LiveSplit.ComponentUtil;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LiveSplit.VoxSplitter {

    public static class MonoCommand {
        public const byte Assemblies = 0;
        public const byte AssemblyImage = 1;
        public const byte ImageName = 2;
        public const byte FindClass = 5;
        public const byte Fields = 6;
        public const byte FieldName = 7;
        public const byte FieldOffset = 8;
        public const byte ClassParent = 9;
        public const byte ClassVTable = 10;
        public const byte StaticField = 11;
        public const byte FieldParent = 12;
        public const byte StaticAddress = 13;
        public const byte Exit = 255;
    }

    public class MonoHelper : IDisposable {

        protected const string AssemblyMonoV1 = "mono.dll";
        protected const string AssemblyMonoV2 = "mono-2.0-bdwgc.dll";
        protected const string AssemblyIl2cpp = "GameAssembly.dll";

        protected const string HelperName = "MonoHelper";

        public bool il2cpp;

        protected NamedPipeClientStream monoPipe;
        protected byte[] buffer = new byte[8];

        protected Memory memory;

        protected Task task;
        protected CancellationTokenSource tokenSource;
        protected CancellationToken token;

        public bool IsCompleted => task?.IsCompleted ?? true;
        
        public int msToWaitAfterStart = 5000;

        public MonoHelper(Memory memory) {
            this.memory = memory;
        }

        public void Run(Action action) {
            if(!IsCompleted) {
                tokenSource.Cancel();
                task.Wait();
            }

            tokenSource = new CancellationTokenSource();
            token = tokenSource.Token;
            task = Task.Factory.StartNew(() => {
                int msSinceGameStart = (int)(DateTime.Now - memory.game.StartTime).TotalMilliseconds;
                if(msSinceGameStart < msToWaitAfterStart) {
                    int msToWait = msToWaitAfterStart - msSinceGameStart;
                    memory.logger?.Log("Game just launched, wait " + msToWait + "ms");
                    Sleep(msToWait);
                }
                try {
                    InitMono();
                    action();
                    memory.logger?.Log("Mono task terminated");
                } catch {
                    memory.logger?.Log("Mono task aborted");
                } finally {
                    ExitMono();
                }
            }, token);
        }

        protected void InitMono() {
            memory.logger?.Log("Waiting for mono module");
            while(true) {
                token.ThrowIfCancellationRequested();

                ProcessModuleWow64Safe module = memory.game.Modules()?.FirstOrDefault(
                    m => m.ModuleName.Equals(AssemblyMonoV1, StringComparison.OrdinalIgnoreCase)
                      || m.ModuleName.Equals(AssemblyMonoV2, StringComparison.OrdinalIgnoreCase)
                      || m.ModuleName.Equals(AssemblyIl2cpp, StringComparison.OrdinalIgnoreCase));

                if(module == null) {
                    Sleep();
                    continue;
                }

                memory.logger?.Log("Mono module: " + module.ModuleName);

                il2cpp = module.ModuleName.Equals(AssemblyIl2cpp, StringComparison.OrdinalIgnoreCase);

                if(!ProcessHasModule(MonoDll)) {
                    memory.logger?.Log("Try to inject MonoHelper");
                    try {
                        InjectDLL(GameDLLPath());
                        if(ProcessHasModule(MonoDll)) {
                            memory.logger?.Log("Injection completed");
                        } else {
                            memory.logger?.Log("Injection failed");
                            continue;
                        }
                    } catch(Exception e) {
                        memory.logger?.Log(e.ToString());
                        continue;
                    }
                } else {
                    memory.logger?.Log("MonoHelper already injected");
                }

                monoPipe = new NamedPipeClientStream(".", HelperName + memory.game.Id, PipeDirection.InOut);
                memory.logger?.Log("Try to connect pipe");
                while(true) {
                    token.ThrowIfCancellationRequested();
                    try {
                        monoPipe.Connect(1000);
                        memory.logger?.Log("Pipe connected");
                        break;
                    } catch(Exception) { }
                }

                memory.logger?.Log("Waiting for main assembly");
                WaitForAssemblyCSharpImage();

                memory.logger?.Log("Mono init done");
                break;
            }
        }

        protected string MonoDll => HelperName + ".dll";
        protected string MonoDllVersion => HelperName + (memory.game.Is64Bit() ? "64.dll" : "32.dll");

        protected string GameDLLPath() {
            Assembly exAssembly = Assembly.GetExecutingAssembly();
            string tmpDllPath = Path.Combine(Path.GetDirectoryName(exAssembly.Location), MonoDll);
            string resPath = exAssembly.Name() + "." + HelperName + "." + MonoDllVersion;
            using(Stream resource = exAssembly.GetManifestResourceStream(resPath)) {
                using(FileStream file = new FileStream(tmpDllPath, FileMode.Create, FileAccess.Write)) {
                    resource.CopyTo(file);
                }
            }
            return tmpDllPath;
        }

        protected void InjectDLL(string path) {
            IntPtr loadLibraryPtr;
            try {
                //GetProcAddress doesn't work right with both 32/64bit so use symbol instead
                loadLibraryPtr = memory.game.GetSymbolAddress("kernel32.dll", "LoadLibraryA");
            } catch(Exception e) {
                throw e;
            }

            IntPtr libraryNamePtr = default;
            uint libraryNameLength = 0;
            IntPtr hThread = default;
            try {
                if((libraryNamePtr = NativeMethods.VirtualAllocEx(memory.game.Handle, default, (uint)path.Length + 1,
                    NativeMethods.AllocationType.Commit | NativeMethods.AllocationType.Reserve,
                    NativeMethods.MemoryProtection.ReadWrite)) == default) {
                    throw new Exception("Couldn't alloc memory");
                }

                byte[] bytes = Encoding.ASCII.GetBytes(path + "\0");
                libraryNameLength = (uint)bytes.Length;
                if(!NativeMethods.WriteProcessMemory(memory.game.Handle, libraryNamePtr, bytes, libraryNameLength, out _)) {
                    throw new Exception("Couldn't write path");
                }

                if((hThread = NativeMethods.CreateRemoteThread(memory.game.Handle, default, 0, loadLibraryPtr, libraryNamePtr, 0, default)) == default) {
                    throw new Exception("Couldn't launch thread");
                }

                NativeMethods.WaitForSingleObject(hThread, 0xFFFFFFFF);
            } finally {
                if(libraryNamePtr != default && libraryNameLength > 0) {
                    NativeMethods.VirtualFreeEx(memory.game.Handle, libraryNamePtr, libraryNameLength, NativeMethods.FreeType.Release);
                }
                if(hThread != default) {
                    NativeMethods.CloseHandle(hThread);
                }
                try {
                    File.Delete(path);
                } catch(Exception) { }
            }
        }

        protected bool ProcessHasModule(string module) {
            return memory.game.Modules()?.Any(m => m.ModuleName.Equals(module, StringComparison.OrdinalIgnoreCase)) ?? false;
        }

        public long[] GetAssemblies() {
            monoPipe.WriteByte(MonoCommand.Assemblies);

            List<long> asmList = new List<long>();
            uint assemblyCount = monoPipe.Read<uint>(buffer);
            for(int i = 0; i < assemblyCount; i++) {
                asmList.Add(monoPipe.Read<long>(buffer));
            }
            return asmList.ToArray();
        }

        public long GetImage(long assembly) {
            monoPipe.WriteByte(MonoCommand.AssemblyImage);

            monoPipe.Write(assembly, buffer);

            return monoPipe.Read<long>(buffer);
        }

        public long FindClass(long image, string className, string nameSpace = "") {
            if(nameSpace == "") {
                int nameSpaceId = className.LastIndexOf('.');
                if(nameSpaceId != -1) {
                    nameSpace = className.Substring(0, nameSpaceId);
                    className = className.Substring(nameSpaceId + 1);
                }
            }

            monoPipe.WriteByte(MonoCommand.FindClass);

            monoPipe.Write(image, buffer);

            monoPipe.Write((ushort)className.Length, buffer);
            if(className.Length > 0) {
                byte[] cnBuffer = Encoding.ASCII.GetBytes(className);
                monoPipe.Write(cnBuffer, cnBuffer.Length);
            }
            monoPipe.Write((ushort)nameSpace.Length, buffer);
            if(nameSpace.Length > 0) {
                byte[] nsBuffer = Encoding.ASCII.GetBytes(nameSpace);
                monoPipe.Write(nsBuffer, nsBuffer.Length);
            }

            return monoPipe.Read<long>(buffer);
        }

        public long[] GetFields(long klass) {
            monoPipe.WriteByte(MonoCommand.Fields);

            monoPipe.Write(klass, buffer);

            List<long> fieldsList = new List<long>();
            uint fieldCount = monoPipe.Read<uint>(buffer);
            for(int i = 0; i < fieldCount; i++) {
                long ptr = monoPipe.Read<long>(buffer);
                if(ptr != 0) {
                    fieldsList.Add(ptr);
                }
            }
            return fieldsList.ToArray();
        }

        public long GetStaticAddress(long klass) {
            if(il2cpp) {
                return default;
            }

            monoPipe.WriteByte(MonoCommand.StaticAddress);

            monoPipe.Write(klass, buffer);

            return monoPipe.Read<long>(buffer);
        }

        public long GetStaticField(long field, long vtable = default) {
            if(!il2cpp && vtable == default) {
                vtable = GetVTable(GetFieldParent(field));
            }

            monoPipe.WriteByte(MonoCommand.StaticField);

            monoPipe.Write(field, buffer);
            if(!il2cpp) {
                monoPipe.Write(vtable, buffer);
            }

            return monoPipe.Read<long>(buffer);
        }

        public string GetImageName(long image) => GetName(MonoCommand.ImageName, image);

        public string GetFieldName(long field) => GetName(MonoCommand.FieldName, field);

        public int GetFieldOffset(long field) => GetValue<int>(MonoCommand.FieldOffset, field);

        public long GetParent(long klass) => GetValue<long>(MonoCommand.ClassParent, klass);

        public long GetVTable(long klass) => GetValue<long>(MonoCommand.ClassVTable, klass);

        public long GetFieldParent(long field) => GetValue<long>(MonoCommand.FieldParent, field);

        protected string GetName(byte command, long pointer) {
            monoPipe.WriteByte(command);

            monoPipe.Write(pointer, buffer);

            ushort strSize = monoPipe.Read<ushort>(buffer);
            byte[] strBuffer = new byte[strSize];
            int readLength = monoPipe.Read(strBuffer, strBuffer.Length);

            return Encoding.ASCII.GetString(strBuffer, 0, readLength);
        }

        protected T GetValue<T>(byte command, long pointer) where T : unmanaged {
            monoPipe.WriteByte(command);

            monoPipe.Write(pointer, buffer);

            return monoPipe.Read<T>(buffer);
        }

        protected void WaitForAssemblyCSharpImage() {
            while(!tokenSource.IsCancellationRequested) {
                if(AssemblyCSharpImage() != default) {
                    return;
                }
                Sleep();
            }
        }

        public long AssemblyCSharpImage() {
            return AssemblyImage("Assembly-CSharp");
        }

        public long AssemblyImage(string name) {
            if(il2cpp) { name += ".dll"; }
            foreach(long assembly in GetAssemblies()) {
                long image = GetImage(assembly);
                string imageName = GetImageName(image);
                if(imageName.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                    return image;
                }
            }
            return default;
        }

        public IEnumerable<long> FieldSequence(long klass, bool includeParents = true) {
            while(klass != 0) {
                foreach(long field in GetFields(klass)) {
                    yield return field;
                }
                if(includeParents) {
                    klass = GetParent(klass);
                } else {
                    break;
                }
            }
        }

        public long FindField(long klass, string fieldName) {
            foreach(long field in FieldSequence(klass)) {
                if(fieldName.Equals(GetFieldName(field), StringComparison.OrdinalIgnoreCase)) {
                    return field;
                }
            }
            return default;
        }

        public int GetFieldOffset(long klass, string fieldName) {
            long field = FindField(klass, fieldName);
            if(field != 0) {
                return GetFieldOffset(field);
            }
            return default;
        }

        public long FindStaticAddress(long image, string className, string instanceName, out long klass, out long field) {
            klass = FindClass(image, className);
            return FindStaticAddress(klass, instanceName, out field);
        }

        public long FindStaticAddress(long klass, string instanceName, out long field) {
            field = FindField(klass, instanceName);
            if(field != 0) {
                return GetStaticAddress(GetFieldParent(field));
            }
            return field = default;
        }

        public long FindStaticField(long image, string className, string fieldName, out long klass, out long field) {
            klass = FindClass(image, className);
            return FindStaticField(klass, fieldName, out field);
        }

        public long FindStaticField(long klass, string fieldName, out long field) {
            field = FindField(klass, fieldName);
            if(field != 0) {
                memory.logger?.Log("Waiting for static field " + fieldName);
                return WaitForStaticField(field);
            }
            return field = default;
        }

        protected long WaitForStaticField(long field) {
            long staticField;
            while(!tokenSource.IsCancellationRequested) {
                staticField = GetStaticField(field);
                if(staticField != default) {
                    return staticField;
                }
                Sleep();
            }
            return default;
        }

        protected void ExitMono() {
            memory.logger?.Log("Exit Mono");
            tokenSource?.Cancel();
            monoPipe?.WriteByte(MonoCommand.Exit);
            monoPipe?.Dispose();
        }

        protected void Sleep(int msTimeout = 50) => Thread.Sleep(msTimeout);
        
        public void Dispose() => ExitMono();
    }

    public class MonoNestedPointerFactory : NestedPointerFactory {

        protected MonoHelper mono;
        
        public MonoNestedPointerFactory(Memory memory, MonoHelper monoHelper) : this(memory, monoHelper, EDerefType.Auto) { }

        public MonoNestedPointerFactory(Memory memory, MonoHelper monoHelper, EDerefType derefType) : base(memory, derefType) {
            mono = monoHelper;
        }

        public StructPointer<IntPtr> MakeStaticAddress(long image, string klassName, string instanceName, out long klass, params int[] il2cppOffsets) {
            klass = mono.FindClass(image, klassName);
            long field = mono.FindField(klass, instanceName);
            int fieldOffset = mono.GetFieldOffset(field);
            if(mono.il2cpp) {
                return Make<IntPtr>((IntPtr)field, il2cppOffsets[0], il2cppOffsets.Skip(1).Append(fieldOffset).ToArray());
            } else {
                return Make<IntPtr>((IntPtr)mono.GetStaticAddress(mono.GetFieldParent(field)), fieldOffset);
            }
        }

        public Pointer MakeStaticField(long image, string klassName, string instanceName, out long klass) {
            if(mono.il2cpp) {
                return MakeBase((IntPtr)mono.FindStaticField(image, klassName, instanceName, out klass, out _));
            } else {
                return Make<IntPtr>((IntPtr)mono.FindStaticField(image, klassName, instanceName, out klass, out long field), mono.GetFieldOffset(field));
            }
        }

        public StructPointer<T> Make<T>(long image, string klassName, string instanceName, int offset, params int[] offsets) where T : unmanaged {
            OutInstanceData(image, klassName, instanceName, offset, offsets, out long ptr, out int ptrOffset, out int[] ptrOffsets);
            return Make<T>((IntPtr)ptr, ptrOffset, ptrOffsets);
        }

        public StructPointer<T> Make<T>(long image, string klassName, string instanceName, string fieldName, params int[] offsets) where T : unmanaged {
            OutInstanceData(image, klassName, instanceName, fieldName, offsets, out long ptr, out int ptrOffset, out int[] ptrOffsets);
            return Make<T>((IntPtr)ptr, ptrOffset, ptrOffsets);
        }

        public StringPointer MakeString(long image, string klassName, string instanceName, int offset, params int[] offsets) {
            OutInstanceData(image, klassName, instanceName, offset, offsets, out long ptr, out int ptrOffset, out int[] ptrOffsets);
            return MakeString((IntPtr)ptr, ptrOffset, ptrOffsets);
        }

        public StringPointer MakeString(long image, string klassName, string instanceName, string fieldName, params int[] offsets) {
            OutInstanceData(image, klassName, instanceName, fieldName, offsets, out long ptr, out int ptrOffset, out int[] ptrOffsets);
            return MakeString((IntPtr)ptr, ptrOffset, ptrOffsets);
        }

        private void OutInstanceData(long image, string klassName, string instanceName, int offset, int[] offsets, out long basePtr, out int ptrOffset, out int[] ptrOffsets) {
            OutInstanceStatic(image, klassName, instanceName, out basePtr, out _, out long field);
            OutInstanceOffsets(field, offset, offsets, out ptrOffset, out ptrOffsets);
        }
        private void OutInstanceData(long image, string klassName, string instanceName, string fieldName, int[] offsets, out long basePtr, out int ptrOffset, out int[] ptrOffsets) {
            OutInstanceStatic(image, klassName, instanceName, out basePtr, out long klass, out long field);
            OutInstanceOffsets(field, mono.GetFieldOffset(klass, fieldName), offsets, out ptrOffset, out ptrOffsets);
        }

        private void OutInstanceStatic(long image, string klassName, string instanceName, out long basePtr, out long klass, out long field) {
            if(mono.il2cpp) {
                basePtr = mono.FindStaticField(image, klassName, instanceName, out klass, out field);
            } else {
                basePtr = mono.FindStaticAddress(image, klassName, instanceName, out klass, out field);
            }
        }

        private void OutInstanceOffsets(long field, int offset, int[] offsets, out int ptrOffset, out int[] ptrOffsets) {
            if(mono.il2cpp) {
                ptrOffset = offset;
                ptrOffsets = offsets;
            } else {
                ptrOffset = mono.GetFieldOffset(field);
                ptrOffsets = offsets.Prepend(offset).ToArray();
            }
        }
    }
}