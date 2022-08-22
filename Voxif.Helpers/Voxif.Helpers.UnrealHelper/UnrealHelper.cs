#define UE_DEBUG

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Voxif.Helpers.MemoryHelper;
using Voxif.Helpers.StructReflector;
using Voxif.IO;
using Voxif.Memory;
using static Voxif.Helpers.MemoryHelper.ScanTarget;

namespace Voxif.Helpers.Unreal {
    public class UnrealHelperTask : HelperTask {
        
        protected readonly ProcessWrapper wrapper;

        public UnrealHelperTask(ProcessWrapper wrapper, Logger logger = null) : base(logger) {
            this.wrapper = wrapper;
        }

        protected override void Log(string msg) => logger?.Log("[Unreal] " + msg);

        public Task Run(Version version, Action<IUnrealHelper> action) {
            return Run(() => {
                UnrealHelperBase unreal;

                //TODO Add UE3
                if(version.Major != 4) {
                    throw new Exception("Unreal version not supported");
                } else {
                    if(version.Minor >= 25) {
                        unreal = new Unreal4_25Helper(wrapper, token, "25", logger);
                    } else if(version.Minor >= 23) {
                        unreal = new Unreal4_23Helper(wrapper, token, "23", logger);
                    } else if(version.Minor >= 22) {
                        unreal = new Unreal4_22Helper(wrapper, token, "22", logger);
                    } else if(version.Minor >= 20) {
                        unreal = new Unreal4_20Helper(wrapper, token, "20", logger);
                    } else if(version.Minor >= 11) {
                        string fileVersion;
                        if(version.Minor >= 18) {
                            fileVersion = "18";
                        } else if(version.Minor >= 14) {
                            fileVersion = "14";
                        } else if(version.Minor >= 13) {
                            fileVersion = "13";
                        } else {
                            fileVersion = "11";
                        }
                        unreal = new Unreal4_11Helper(wrapper, token, fileVersion, logger);
                    } else if(version.Minor >= 8) {
                        unreal = new Unreal4_8Helper(wrapper, token, "8", logger);
                    } else {
                        unreal = new Unreal4_0Helper(wrapper, token, "0", logger);
                    }
                }

                while(true) {
                    token.ThrowIfCancellationRequested();
                    if(unreal.ScanTask.IsCompleted) {
                        break;
                    }
                    Sleep();
                }

                action(unreal);
            });
        }

        private abstract class UnrealHelperBase : IUnrealHelper {

            protected CancellationToken token;
            public ScanHelperTask ScanTask { get; }

            protected readonly ProcessWrapper game;

            protected readonly Logger logger;

            protected IStructReflector data;

            protected IntPtr namesPtr;
            protected IntPtr objectsPtr;

            protected UnrealHelperBase(ProcessWrapper game, CancellationToken token, string fileName, string fileVersion, Logger logger = null) {
                this.game = game;
                this.token = token;
                this.logger = logger;
                Log($"Version: {fileName}.{fileVersion}");
                data = StructReflector.StructReflector.Load("Voxif.Helpers.UnrealHelper.Unreal" + fileName + "_" + fileVersion, game.PointerSize);
                ScanTask = new ScanHelperTask(game, logger);
                var fname = FNamesTarget;
                fname.OnFound += OnFNamesFound;
                var uobject = UObjectsTarget;
                uobject.OnFound += OnUObjectsFound;
                ScanTask.Run(
                    new ScannableData { {
                        game.Process.MainModule.ModuleName,
                        new Dictionary<string, ScanTarget> {
                            { "fname", fname },
                            { "uobject", uobject}
                        }
                    } }
                );
            }

            protected abstract ScanTarget FNamesTarget { get; }
            protected abstract ScanTarget UObjectsTarget { get; }

            protected abstract OnScanFoundCallback OnFNamesFound { get; }
            protected abstract OnScanFoundCallback OnUObjectsFound { get; }

            public void Log(string msg) => logger?.Log("[Unreal] " + msg);

            protected struct FName {
                public int index;
                public string name;

                public FName(int index, string name) {
                    this.index = index;
                    this.name = name;
                }
            }

            protected abstract IEnumerable<FName> FNameSequence();
            protected abstract IEnumerable<IntPtr> UObjectSequence();

            public abstract Dictionary<string, IntPtr> GetUObjects(params string[] names);
            public abstract IntPtr GetUObject(string name);
            public abstract IntPtr GetUObject(int fname);

            public abstract Dictionary<string, int> GetFNames(params string[] names);
            public abstract int GetFName(string name);

            public abstract int GetFieldOffset(IntPtr uobject, string fieldName);

#if UE_DEBUG
            public abstract void Debug(string gameName);
#endif
        }

        private abstract class Unreal4HelperBase : UnrealHelperBase {

            protected Unreal4HelperBase(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : base(game, token, "4", fileVersion, logger) { }

            public override IntPtr GetUObject(string name) {
                Log("Looking for object: " + name);
                while(true) {
                    token.ThrowIfCancellationRequested();
                    foreach(IntPtr uobject in UObjectSequence()) {
                        if(UObjectName(uobject) == name && UObjectClassName(uobject) == name) {
                            Log("  -> " + uobject.ToString("X"));
                            return uobject;
                        }
                    }
                    Sleep();
                }
            }

            public override IntPtr GetUObject(int fname) {
                Log("Looking for object: " + fname);
                while(true) {
                    token.ThrowIfCancellationRequested();
                    foreach(IntPtr uobject in UObjectSequence()) {
                        if(UObjectFName(uobject) == fname && UObjectFName(UObjectClass(uobject)) == fname) {
                            Log("  -> " + uobject.ToString("X"));
                            return uobject;
                        }
                    }
                    Sleep();
                }
            }

            public override int GetFName(string name) {
                Log("Looking for name: " + name);
                while(true) {
                    token.ThrowIfCancellationRequested();
                    foreach(FName fname in FNameSequence()) {
                        if(fname.name == name) {
                            Log("  -> " + fname.name + " " + fname.index);
                            return fname.index;
                        }
                    }
                    Sleep();
                }
            }

            public override Dictionary<string, int> GetFNames(params string[] names) {
                Log("Looking for names: " + String.Join(", ", names));
                var dict = new Dictionary<string, int>(names.Length);
                while(true) {
                    token.ThrowIfCancellationRequested();
                    foreach(FName fname in FNameSequence()) {
                        if(Array.IndexOf(names, fname.name) != -1 && !dict.ContainsKey(fname.name)) {
                            dict.Add(fname.name, fname.index);
                            Log("  -> " + fname.name + " " + fname.index);
                            if(dict.Count == names.Length) {
                                return dict;
                            }
                        }
                    }
                    Sleep();
                }
            }

            public override Dictionary<string, IntPtr> GetUObjects(params string[] names) => throw new NotImplementedException();

#if UE_DEBUG
            public override void Debug(string gameName) {

                DebugNames();
                DebugObjects();

                void DebugNames() {
                    Log("Debug FNames");
                    string namesPath = $"_{gameName}_Names.log";
                    using(StreamWriter write = new StreamWriter(namesPath)) {
                        foreach(FName fname in FNameSequence()) {
                            string dbgName = $"{fname.index,-7} {fname.name}";
                            write.WriteLine(dbgName);
                        }
                    }
                }

                void DebugObjects() {
                    Log("Debug GObjects");
                    var dict = new Dictionary<string, Tuple<List<IntPtr>, string>>();
                    foreach(IntPtr uobject in UObjectSequence()) {

                        string className = UObjectClassName(uobject);
                        IntPtr uobject2 = uobject;
                        if(className.EndsWith("Property") || className.Equals("Function") || className.Equals("Package") || className.Equals("Enum")) {
                            continue;
                        }

                        string objName = UObjectName(uobject);
                        //if(objName.StartsWith("Default__")) {
                        //    continue;
                        //}

                        objName = className + " " + objName;

                        string superName = UObjectName(SuperStruct(uobject2));
                        if(!String.IsNullOrEmpty(superName)) {
                            objName += " : " + superName;
                        }

                        if(className != "Class" && className != "ScriptStruct") {
                            uobject2 = UObjectClass(uobject);
                        }

                        if(dict.TryGetValue(objName, out var tmp)) {
                            tmp.Item1.Add(uobject);
                        } else {
                            int parentFieldsSize = game.Read<int>(SuperStruct(uobject2) + data.GetOffset("UStruct", "PropertiesSize"));
                            string dbgFields = "";
                            foreach(IntPtr field in FieldSequence(uobject2)) {
                                int offset = game.Read<int>(field + data.GetOffset("Property", "Offset_Internal"));
                                if(offset < parentFieldsSize) {
                                    break;
                                }
                                dbgFields += $"  {offset,-4:X} {PropertyType(field),-32} {FieldName(field)}" + Environment.NewLine;
                            }
                            dict.Add(objName, new Tuple<List<IntPtr>, string>(new List<IntPtr> { uobject }, dbgFields));
                        }
                    }
                    string objectsPath = $"_{gameName}_Objects.log";
                    using(StreamWriter write = new StreamWriter(objectsPath)) {
                        foreach(var kvp in dict) {
                            foreach(var ptr in kvp.Value.Item1) {
                                write.WriteLine(ptr.ToString("X8"));
                            }
                            write.WriteLine(kvp.Key);
                            write.WriteLine(kvp.Value.Item2);
                        }
                    }
                }
            }
#endif

            protected virtual IntPtr SuperStruct(IntPtr uobject) {
                return game.Read<IntPtr>(uobject + data.GetOffset("UStruct", "SuperStruct"));
            }

            public override int GetFieldOffset(IntPtr uobject, string fieldName) {
                foreach(IntPtr field in FieldSequence(uobject)) {
                    if(fieldName.Equals(UObjectName(field))) {
                        return game.Read<int>(field + data.GetOffset("Property", "Offset_Internal"));
                    }
                }
                return default;
            }

            protected abstract string FNameEntryName(int index);

            protected virtual IntPtr UObjectClass(IntPtr uobject) {
                return game.Read<IntPtr>(uobject + data.GetOffset("UObjectBase", "Class"));
            }
            protected virtual string UObjectClassName(IntPtr uobject) {
                return UObjectName(UObjectClass(uobject));
            }
            protected virtual string UObjectName(IntPtr uobject) {
                return FNameEntryName(UObjectFName(uobject));
            }
            protected virtual int UObjectFName(IntPtr uobject) {
                //Assume FName Index is always at offset 0
                return game.Read<int>(uobject + data.GetOffset("UObjectBase", "Name"));
            }

            protected virtual IntPtr FieldClass(IntPtr property) => UObjectClass(property);
            protected virtual string FieldClassName(IntPtr uobject) => FieldName(FieldClass(uobject));
            protected virtual string FieldName(IntPtr property) => UObjectName(property);
            protected virtual int FieldFName(IntPtr property) => UObjectFName(property);

            protected virtual IEnumerable<IntPtr> FieldSequence(IntPtr uobject) {
                int offsetPropertyNext = data.GetOffset("Property", "PropertyLinkNext");
                //IntPtr field = processWrapper.Read<IntPtr>(uobject + data.GetOffset("UStruct", "Children"));
                IntPtr field = game.Read<IntPtr>(uobject + data.GetOffset("UStruct", "PropertyLink"));
                while(field != default) {
                    yield return field;
                    field = game.Read<IntPtr>(field + offsetPropertyNext);
                }
            }

            protected virtual string PropertyType(IntPtr property) {
                string type = FieldClassName(property);
                switch(type) {
                    case "ByteProperty":
                        IntPtr enumPtr = game.Read<IntPtr>(property + data.GetOffset("ByteProperty", "Enum"));
                        return enumPtr == default ? "byte" : "enum " + UObjectName(enumPtr);

                    case "UInt16Property": return "ushort";
                    case "UInt32Property": return "uint";
                    case "UInt64Property": return "ulong";

                    case "Int8Property": return "sbyte";
                    case "Int16Property": return "short";
                    case "IntProperty": return "int";
                    case "Int64Property": return "long";

                    case "FloatProperty": return "float";
                    case "DoubleProperty": return "double";

                    case "BoolProperty": return "bool";

                    case "StrProperty": return "char[]*";
                    case "NameProperty": return "FName";
                    //TODO
                    //case "TextProperty": return "";

                    case "ObjectProperty":
                        return UObjectName(game.Read<IntPtr>(property + data.GetOffset("ObjectPropertyBase", "PropertyClass"))) + "*";
                    case "LazyObjectProperty":
                        return "lazy " + UObjectName(game.Read<IntPtr>(property + data.GetOffset("ObjectPropertyBase", "PropertyClass"))) + "*";
                    case "SoftObjectProperty":
                        return "soft " + UObjectName(game.Read<IntPtr>(property + data.GetOffset("ObjectPropertyBase", "PropertyClass"))) + "*";
                    case "WeakObjectProperty":
                        return "weak " + UObjectName(game.Read<IntPtr>(property + data.GetOffset("ObjectPropertyBase", "PropertyClass"))) + "*";

                    case "ClassProperty":
                        return UObjectName(game.Read<IntPtr>(property + data.GetOffset("ClassProperty", "MetaClass"))) + "*";

                    case "StructProperty":
                        return UObjectName(game.Read<IntPtr>(property + data.GetOffset("StructProperty", "Struct")));

                    case "ArrayProperty":
                        return "TArray<" + PropertyType(game.Read<IntPtr>(property + data.GetOffset("ArrayProperty", "Inner"))) + ">";
                    case "SetProperty":
                        return "TSet<" + PropertyType(game.Read<IntPtr>(property + data.GetOffset("SetProperty", "ElementProp"))) + ">";
                    case "MapProperty":
                        return "TMap<" + PropertyType(game.Read<IntPtr>(property + data.GetOffset("MapProperty", "KeyProp")))
                                + ", " + PropertyType(game.Read<IntPtr>(property + data.GetOffset("MapProperty", "ValueProp"))) + ">";

                    case "InterfaceProperty":
                        return UObjectName(game.Read<IntPtr>(property + data.GetOffset("InterfaceProperty", "InterfaceClass")));

                    case "EnumProperty":
                        return "enum " + UObjectName(game.Read<IntPtr>(property + data.GetOffset("EnumProperty", "Enum")));

                    case "DelegateProperty":
                    case "MulticastDelegateProperty":
                    case "MulticastInlineDelegateProperty":
                    case "MulticastSparseDelegateProperty":
                        return "delegate";

                    default: return type;
                }
            }
        }

        private class Unreal4_0Helper : Unreal4HelperBase {

            public Unreal4_0Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : base(game, token, fileVersion, logger) { }

            protected override ScanTarget FNamesTarget {
                get => game.Is64Bit ? new ScanTarget(0x7, "48 83 EC 28 48 8B 05 ???????? 48 85 C0 75 ?? B9 08??0000 48 89 5C 24 20 E8")
                                    : new ScanTarget(0x1, "A1 ???????? 85 C0 75 ?? 56 68 08??0000 E8");
            }

            protected override OnScanFoundCallback OnFNamesFound {
                get => (ptr, version) => {
                    namesPtr = game.FromAssemblyAddress(ptr);
                    Log("Names " + namesPtr.ToString("X"));
                };
            }

            protected override ScanTarget UObjectsTarget {
                get => game.Is64Bit ? new ScanTarget(0, "40 53 48 83 EC ?? 48 8D 05 ???????? 48 8B D9 48 89 01 80 3D ???????? 00 74 ?? 48 83 79 10 00 74 ?? 80 3D ???????? 00 75")
                                    : new ScanTarget(0, "56 8B F1 C7 06 ???????? 80 3D ???????? 00 74 ?? 83 7E 0C 00 74 ?? 80 3D ???????? 00 75");
            }

            protected override OnScanFoundCallback OnUObjectsFound {
                get => (ptr, version) => {

                    var tmpScanner = new SignatureScanner(game.Process, ptr, 0x100);
                    var tmpTarget = new ScanTarget().AddSignature(0x1, "B9 ???????? 56 E8 ???????? 5E C3")
                                                    .AddSignature(0x2, "56 B9 ???????? E8 ???????? 5E C3");
                    var tmp = tmpScanner.Scan(tmpTarget);
                    objectsPtr = game.FromAbsoluteAddress(tmp);

                    Log("Objects " + objectsPtr.ToString("X"));
                };
            }

            protected virtual IntPtr FNamesChunk {
                get => game.Read<IntPtr>(namesPtr + data.GetOffset("TStaticIndirectArrayThreadSafeRead<>", "Chunks"));
            }

            protected virtual IntPtr UObjectsObjObjects {
                get => objectsPtr + data.GetOffset("FUObjectArray", "ObjObjects");
            }
            protected virtual IntPtr UObjectsData {
                get => game.Read<IntPtr>(UObjectsObjObjects + data.GetOffset("TArray<>", "AllocatorInstance"));
            }
            protected virtual int UObjectsSize {
                get => game.Read<int>(UObjectsObjObjects + data.GetOffset("TArray<>", "ArrayNum"));
            }

            protected override IEnumerable<FName> FNameSequence() {
                IntPtr chunk;
                int chunkNb = -1;
                IntPtr namesChunks = FNamesChunk;
                while((chunk = game.Read<IntPtr>(namesChunks + (++chunkNb) * game.PointerSize)) != default) {
                    for(int i = 0; i < 16384; i++) {
                        IntPtr fname = game.Read<IntPtr>(chunk + i * game.PointerSize);
                        if(fname == default) {
                            continue;
                        }

                        int index = game.Read<int>(fname + data.GetOffset("FNameEntry", "Index")) >> 1;
                        string name = FNameEntryName(fname);
                        //Console.WriteLine(chunkNb + " " + (chunk + i * memory.PointerSize).ToString("X8") + " " + fname.ToString("X8") + " " + index + " " + name);
                        yield return new FName(index, name);
                    }
                }
            }

            protected override string FNameEntryName(int index) {
                if(index == 0) {
                    return "";
                }
                const int maxChunkSize = 16384;
                IntPtr chunk = game.Read<IntPtr>(FNamesChunk + index / maxChunkSize * game.PointerSize);
                IntPtr fname = game.Read<IntPtr>(chunk + index % maxChunkSize * game.PointerSize);
                return FNameEntryName(fname);
            }

            protected virtual string FNameEntryName(IntPtr fNameEntry) {
                bool isWide = (game.Read<int>(fNameEntry + data.GetOffset("FNameEntry", "Index")) & 1) == 1;
                return game.ReadString(fNameEntry + data.GetOffset("FNameEntry", "Name"), isWide ? EStringType.UTF16 : EStringType.UTF8);
            }

            protected override IEnumerable<IntPtr> UObjectSequence() {
                int size = UObjectsSize;
                IntPtr objects = UObjectsData;
                for(int i = 0; i < size; i++) {
                    IntPtr uobject = game.Read<IntPtr>(objects + i * game.PointerSize);
                    if(uobject == default) {
                        continue;
                    }
                    yield return uobject;
                }
            }
        }

        private class Unreal4_8Helper : Unreal4_0Helper {

            public Unreal4_8Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : base(game, token, fileVersion, logger) { }

            protected override ScanTarget UObjectsTarget {
                get => game.Is64Bit ? new ScanTarget(0x4A, "40 53 48 83 EC ?? 48 8D 05 ???????? 48 8B D9 48 89 01 80 3D ???????? 00 74 ?? 48 83 79 10 00 74 ?? 80 3D ???????? 00 75")
                                    : new ScanTarget(0x40, "56 8B F1 C7 06 ???????? 80 3D ???????? 00 74 ?? 83 7E 0C 00 74 ?? 80 3D ???????? 00 75");
            }

            protected override OnScanFoundCallback OnUObjectsFound {
                get => (ptr, version) => {
                    IntPtr getArrayFunc = game.FromRelativeAddress(ptr);
                    var tmpScanner = new SignatureScanner(game.Process, getArrayFunc, 0x100);
                    var tmp = tmpScanner.Scan(game.Is64Bit ? new ScanTarget(0x3, "48 8D 05 ???????? 48 83 C4 ?? 5B C3")
                                                           : new ScanTarget(0x4, "83 C4 ?? B8 ???????? C3"));
                    objectsPtr = game.FromAssemblyAddress(tmp);
                    Log("Objects " + objectsPtr.ToString("X"));
                };
            }

            protected override IntPtr UObjectsData {
                get => UObjectsObjObjects + data.GetOffset("TStaticIndirectArrayThreadSafeRead<>", "Chunks");
            }
            protected override int UObjectsSize {
                get => throw new NotImplementedException();
            }

            protected override IEnumerable<IntPtr> UObjectSequence() {
                int chunkNb = -1;
                IntPtr chunk;
                IntPtr objChunks = UObjectsData;
                while((chunk = game.Read<IntPtr>(objChunks + (++chunkNb) * game.PointerSize)) != default) {
                    for(int i = 0; i < 16384; i++) {
                        IntPtr uobject = game.Read<IntPtr>(chunk + i * game.PointerSize);
                        if(uobject == default) {
                            continue;
                        }
                        yield return uobject;
                    }
                }
            }
        }

        private class Unreal4_11Helper : Unreal4_8Helper {

            public Unreal4_11Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : base(game, token, fileVersion, logger) { }

            protected override ScanTarget UObjectsTarget {
                get => game.Is64Bit ? new ScanTarget(0x4F, "40 53 48 83 EC ?? 48 8D 05 ???????? 48 8B D9 48 89 01 80 3D ???????? 00 74 ?? 48 83 79 10 00 74 ?? 80 3D ???????? 00 75")
                                    : new ScanTarget(0x3F, "56 8B F1 C7 06 ???????? 80 3D ???????? 00 74 ?? 83 7E 0C 00 74 ?? 80 3D ???????? 00 75");
            }

            protected override OnScanFoundCallback OnUObjectsFound {
                get => (ptr, version) => {
                    objectsPtr = game.FromAssemblyAddress(ptr);
                    Log("Objects " + objectsPtr.ToString("X"));
                };
            }

            protected override IntPtr UObjectsData {
                get => game.Read<IntPtr>(UObjectsObjObjects + data.GetOffset("FFixedUObjectArray", "Objects"));
            }
            protected override int UObjectsSize {
                get => game.Read<int>(UObjectsObjObjects + data.GetOffset("FFixedUObjectArray", "NumElements"));
            }

            protected override IEnumerable<IntPtr> UObjectSequence() {
                int fuobjectSize = data.GetSelfAlignedSize("FUObjectItem");
                int size = UObjectsSize;
                IntPtr objects = UObjectsData;
                for(int i = 0; i < size; i++) {
                    IntPtr uobject = game.Read<IntPtr>(objects + i * fuobjectSize);
                    if(uobject == default) {
                        continue;
                    }
                    yield return uobject;
                }
            }
        }

        private class Unreal4_20Helper : Unreal4_11Helper {

            public Unreal4_20Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : base(game, token, fileVersion, logger) { }

            protected override IntPtr UObjectsData {
                get => game.Read<IntPtr>(UObjectsObjObjects + data.GetOffset("FChunkedFixedUObjectArray", "Objects"));
            }
            protected override int UObjectsSize {
                get => game.Read<int>(UObjectsObjObjects + data.GetOffset("FChunkedFixedUObjectArray", "NumElements"));
            }

            protected override IEnumerable<IntPtr> UObjectSequence() {
                int fuobjectSize = data.GetSelfAlignedSize("FUObjectItem");
                int chunkNb = -1;
                IntPtr chunk;
                IntPtr objChunks = UObjectsData;
                while((chunk = game.Read<IntPtr>(objChunks + (++chunkNb) * game.PointerSize)) != default) {
                    for(int i = 0; i < 65536; i++) {
                        IntPtr uobject = game.Read<IntPtr>(chunk + i * fuobjectSize);
                        if(uobject == default) {
                            continue;
                        }
                        yield return uobject;
                    }
                }
            }
        }

        private class Unreal4_22Helper : Unreal4_20Helper {

            public Unreal4_22Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : base(game, token, fileVersion, logger) { }

            protected override ScanTarget UObjectsTarget {
                get => game.Is64Bit ? new ScanTarget(0x9, "48 8B D1 45 33 C0 48 8D 0D ?? ?? ?? ?? E9 ?? ?? ?? ??")
                                    : new ScanTarget(0x7, "6A 00 FF 74 24 08 B9 ?? ?? ?? ?? E8 ?? ?? ?? ?? C3");
            }
        }

        private class Unreal4_23Helper : Unreal4_22Helper {

            public Unreal4_23Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : base(game, token, fileVersion, logger) { }

            protected override ScanTarget FNamesTarget {
                get => game.Is64Bit ? new ScanTarget(0x5, "74 09 48 8D 15 ???????? EB 16 48 8D 0D ???????? E8 ???????? 48 8B D0 C6 05 ???????? 01")
                                    : new ScanTarget(0x3, "74 07 B8 ???????? EB 11 B9 ???????? E8 ???????? C6 05 ???????? 01");
            }

            protected override IntPtr FNamesChunk {
                get => namesPtr + data.GetOffset("FNamePool", "entries") + data.GetOffset("FNameEntryAllocator", "Blocks");
            }

            protected override IEnumerable<FName> FNameSequence() {
                IntPtr chunk;
                int chunkNb = -1;
                IntPtr namesChunks = FNamesChunk;
                while((chunk = game.Read<IntPtr>(namesChunks + (++chunkNb) * game.PointerSize)) != default) {
                    IntPtr cursor = chunk;
                    int block = chunkNb << 16;
                    int offset;
                    while((offset = (int)((long)cursor - (long)chunk) >> 1) < 0xFFFF) {
                        int length = FNameEntryHeaderLength(cursor, out _);
                        if(length == default) {
                            break;
                        }
                        int index = block | offset;
                        string name = FNameEntryName(cursor);
                        //Console.WriteLine(chunkNb + " " + cursor.ToString("X8") + " " + index + " " + name);
                        yield return new FName(index, name);
                        cursor += 2 + length;
                        if(((long)cursor & 1) == 1) {
                            cursor += 1;
                        }
                    }
                }
            }

            protected override string FNameEntryName(int index) {
                if(index == 0) {
                    return "";
                }
                IntPtr cursor = game.Read<IntPtr>(FNamesChunk + game.PointerSize * (index >> 16)) + ((index & 0xFFFF) << 1);
                return FNameEntryName(cursor);
            }

            protected override string FNameEntryName(IntPtr fNameEntry) {
                int length = FNameEntryHeaderLength(fNameEntry, out bool isWide);
                return game.ReadString(fNameEntry + data.GetOffset("FNameEntry", "Name"), length, isWide ? EStringType.UTF16 : EStringType.UTF8);
            }

            protected virtual int FNameEntryHeaderLength(IntPtr fNameEntry, out bool isWide) {
                ushort header = game.Read<ushort>(fNameEntry);
                int length = header >> 6;
                isWide = (header & 1) == 1;
                if(isWide) {
                    length *= 2;
                }
                return length;
            }
        }

        private class Unreal4_25Helper : Unreal4_23Helper {
            public Unreal4_25Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : base(game, token, fileVersion, logger) { }

            protected override IntPtr FieldClass(IntPtr property) {
                return game.Read<IntPtr>(property + data.GetOffset("FField", "Class"));
            }
            protected override string FieldClassName(IntPtr property) {
                return FNameEntryName(game.Read<int>(FieldClass(property) + data.GetOffset("FFieldClass", "Name")));
            }
            protected override string FieldName(IntPtr property) {
                return FNameEntryName(FieldFName(property));
            }
            protected override int FieldFName(IntPtr property) {
                //Assume FName Index is always at offset 0
                return game.Read<int>(property + data.GetOffset("FField", "Name"));
            }

            protected override IEnumerable<IntPtr> FieldSequence(IntPtr uobject) {
                int offsetPropertyNext = data.GetOffset("Property", "PropertyLinkNext");
                IntPtr field = game.Read<IntPtr>(uobject + data.GetOffset("UStruct", "ChildProperties"));
                while(field != default) {
                    yield return field;
                    field = game.Read<IntPtr>(field + offsetPropertyNext);
                }
            }
        }

#if DEBUG
        public class FNameLookup {
            private readonly HashSet<string> missingNames;
            private readonly Dictionary<int, string> indexLookup;
            private readonly Dictionary<string, int> nameLookup;

            public FNameLookup(params string[] names) {
                missingNames = new HashSet<string>(names);
                indexLookup = new Dictionary<int, string>(names.Length);
                nameLookup = new Dictionary<string, int>(names.Length);
            }

            public void AddEntry(int index, string name) {
                if(!missingNames.Remove(name)) {
                    nameLookup.Add(name, index);
                    indexLookup.Add(index, name);
                }
            }

            public int Count => indexLookup.Count;
            public int MissingCount => nameLookup.Count - indexLookup.Count;

            public bool TryGetValue(int index, out string name) => indexLookup.TryGetValue(index, out name);
            public bool TryGetValue(string name, out int index) => nameLookup.TryGetValue(name, out index);

            public string this[int index] => indexLookup[index];
            public int this[string name] => nameLookup[name];
        }
#endif
    }

    public interface IUnrealHelper {
        Dictionary<string, IntPtr> GetUObjects(params string[] names);
        IntPtr GetUObject(string name);
        IntPtr GetUObject(int fname);

        Dictionary<string, int> GetFNames(params string[] names);
        int GetFName(string name);

        int GetFieldOffset(IntPtr uobject, string fieldName);
#if UE_DEBUG
        void Debug(string gameName);
#endif
    }
}