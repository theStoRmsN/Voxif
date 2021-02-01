using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Voxif.Helpers.StructReflector {
    public static class StructReflector {
        private static ReflectedList Prepare(string resourcePath, ReflectedList refListSource = null) {
            ReflectedList refListDest = refListSource ?? new ReflectedList();

            Assembly assembly = Assembly.GetExecutingAssembly();
            using(StreamReader reader = new StreamReader(assembly.GetManifestResourceStream(resourcePath + ".txt"))) {

                string baseVersion = reader.ReadLine();
                if(!String.IsNullOrEmpty(baseVersion)) {
                    Prepare(baseVersion, refListDest);
                }

                Console.WriteLine("Prepare " + resourcePath);

                ReflectedList refList = new ReflectedList();
                int listId = -1;
                string line;
                bool overwriteStruct = false;
                while((line = reader.ReadLine()) != null) {
                    int commentId = line.IndexOf("//");
                    if(commentId != -1) {
                        line = line.Substring(0, commentId);
                    }

                    string lineTrim = line.Trim();
                    if(lineTrim.Length == 0) {
                        continue;
                    }

                    if(line[0] != ' ') {
                        bool found = false;
                        int superIndex;
                        if(refListDest.Contains(lineTrim, out listId)) {
                            found = true;
                            overwriteStruct = true;
                            for(int i = refList.Count - 1; i >= 0; i--) {
                                refListDest.Insert(listId, refList[i]);
                            }
                            listId += refList.Count;
                            refList.Clear();
                            refListDest[listId].fields.Clear();
                        } else if((superIndex = lineTrim.IndexOf(':')) != -1) {
                            for(int refId = 0; refId < refListDest.Count; refId++) {
                                string currentLine = refListDest[refId].name;
                                if(currentLine.Length > superIndex && lineTrim.Substring(0, superIndex) == currentLine.Substring(0, superIndex)) {
                                    found = true;
                                    overwriteStruct = true;
                                    for(int i = refList.Count - 1; i >= 0; i--) {
                                        refListDest.Insert(refId, refList[i]);
                                    }
                                    listId = refId + refList.Count;
                                    refList.Clear();
                                    refListDest[listId] = new ReflectedData(lineTrim, new List<string>());
                                    break;
                                }
                            }
                        }
                        if(!found) {
                            overwriteStruct = false;
                            listId = refList.Count;
                            refList.Add(new ReflectedData(lineTrim, new List<string>()));
                        }
                    } else {
                        if(overwriteStruct) {
                            refListDest[listId].fields.Add(lineTrim);
                        } else {
                            refList[listId].fields.Add(lineTrim);
                        }
                    }
                }

                for(int i = 0; i < refList.Count; i++) {
                    refListDest.Add(refList[i]);
                }
            }
            return refListDest;
        }

        public static IStructReflector Load(string resourcePath, int pointerSize) {
            ReflectedList reflectedList = Prepare(resourcePath);

            StructureDict structDict = new StructureDict();
            int lastOffset;
            int remainingBitFields;

            foreach(ReflectedData data in reflectedList) {
                string[] structNames = data.name.Replace(" ", "").Split(':');
                string structName = RemoveGeneric(structNames[0]);
                string baseName = structNames.Length > 1 ? RemoveGeneric(structNames[1]) : null;

                string displayName = Environment.NewLine + structName;
                if(baseName != null) {
                    displayName += " : " + baseName;
                    lastOffset = structDict[baseName].Size;
                } else {
                    lastOffset = 0;
                }
                Console.WriteLine(displayName);

                VariableDict varDict = new VariableDict(lastOffset);
                structDict.Add(structName, varDict);
                remainingBitFields = -1;

                foreach(string varLine in data.fields) {
                    VarData varData = HandleVariable(varLine, pointerSize, structDict, ref remainingBitFields, ref lastOffset);
                    varDict.Add(varData);
                }
            }
            Console.WriteLine();
            return structDict;
        }

        private static VarData HandleVariable(string varLine, int pointerSize, StructureDict structDict, ref int remainingBitFields, ref int lastOffset) {
            int varId = varLine.IndexOf(' ');
            string type = RemoveGeneric(varLine.Substring(0, varId));
            string name = varLine.Substring(varId + 1);

            bool isPointer = false;
            int arrayDim = -1;
            int bitField = -1;
            string typeName;

            int pointerId = type.IndexOf('*');
            if(pointerId != -1) {
                isPointer = true;
            }

            int arrayId = type.IndexOf('[');
            if(arrayId != -1) {
                int endId = type.IndexOf(']', arrayId);
                int startId = arrayId + 1;
                arrayDim = Int32.TryParse(type.Substring(startId, endId - startId), out int resNb) ? resNb : 1;
            }

            int bitId = type.IndexOf(':');
            if(bitId != -1) {
                bitField = Int32.Parse(type.Substring(bitId + 1));
            }

            if(pointerId != -1 || arrayId != -1 || bitId != -1) {
                typeName = type.Substring(0, GetLowestId(pointerId, arrayId, bitId));

                int GetLowestId(params int[] ids) {
                    int lowest = Int32.MaxValue;
                    foreach(int id in ids) {
                        if(id != -1 && id < lowest) {
                            lowest = id;
                        }
                    }
                    return lowest;
                }
            } else {
                typeName = type;
            }

            VarData result;
            if(bitField != -1) {
                int alignSize = 0;
                if(remainingBitFields < 0) {
                    IsBuiltinTypeSize(typeName, pointerSize, out int typeSize, out alignSize);
                    remainingBitFields = typeSize * 8;
                    lastOffset = AlignByte(lastOffset, alignSize);
                }

                result = new VarData(name, lastOffset, BitsToBytes(bitField), alignSize);

                int oldByte = BitsToBytes(remainingBitFields);
                remainingBitFields -= bitField;
                int curByte = BitsToBytes(remainingBitFields);
                if(oldByte > curByte) {
                    lastOffset += oldByte - curByte;
                }
            } else {
                if(remainingBitFields > 0) {
                    lastOffset += BitsToBytes(remainingBitFields);
                    remainingBitFields = -1;
                }

                int typeSize = 0;
                int alignSize = 0;
                if(arrayDim != 0) {
                    bool isStruct = false;
                    if(isPointer) {
                        typeSize = alignSize = pointerSize;
                    } else if(IsBuiltinTypeSize(typeName, pointerSize, out typeSize, out alignSize)) {
                        ;
                    } else if(IsKnownStructure(structDict, typeName, out typeSize, out alignSize)) {
                        isStruct = true;
                    } else {
                        typeSize = alignSize = pointerSize;
                    }
                    lastOffset = AlignByte(lastOffset, alignSize);

                    if(arrayDim > 0) {
                        if(isStruct) {
                            typeSize = AlignByte(typeSize, alignSize);
                        }
                        typeSize *= arrayDim;
                    }
                }

                result = new VarData(name, lastOffset, typeSize, alignSize);

                lastOffset += typeSize;
            }
            Console.WriteLine($"  {result.offset,-4:X} {result.size,-4:X} {type,-32} {result.name}");
            return result;
        }

        private static string RemoveGeneric(string name) {
            int gIndex = name.IndexOf('<');
            if(gIndex != -1) {
                name = name.Substring(0, gIndex + 1) + name.Substring(name.LastIndexOf('>'));
            }
            return name;
        }

        // Visual C++ types (with C# names)
        private static bool IsBuiltinTypeSize(string name, int pointerSize, out int typeSize, out int alignSize) {
            switch(name) {
                case "bool":
                case "byte":
                case "sbyte":
                    typeSize = alignSize = 1;
                    return true;
                case "short":
                case "ushort":
                case "char":
                    typeSize = alignSize = 2;
                    return true;
                case "int":
                case "uint":
                case "float":
                    typeSize = alignSize = 4;
                    return true;
                case "long":
                case "ulong":
                    typeSize = 8;
                    alignSize = pointerSize;
                    return true;
                case "double":
                    //8-4 on linux and 8-8 with -malign-double
                    typeSize = alignSize = 8;
                    return true;
                case "decimal":
                    //highly platform dependant
                    typeSize = alignSize = 8;
                    return true;
                default:
                    typeSize = alignSize = 0;
                    return false;
            }
        }

        private static bool IsKnownStructure(StructureDict structDict, string name, out int typeSize, out int alignSize) {
            if(structDict.ContainsKey(name)) {
                typeSize = structDict[name].Size;
                alignSize = structDict[name].Alignment;
                return true;
            } else {
                typeSize = 0;
                alignSize = 0;
                return false;
            }
        }

        private static int AlignByte(int total, int alignWith) {
            if(alignWith != 0) {
                int align = total % alignWith;
                if(align != 0) {
                    total += alignWith - align;
                }
            }
            return total;
        }

        private static int BitsToBytes(int value) {
            int result = value / 8;
            if(value % 8 != 0) {
                result++;
            }
            return result;
        }

        private class ReflectedList : List<ReflectedData> {
            public bool Contains(string name, out int index) {
                for(int i = 0; i < Count; i++) {
                    if(this[i].name.Equals(name)) {
                        index = i;
                        return true;
                    }
                }
                index = -1;
                return false;
            }
        }

        private struct ReflectedData {
            public string name;
            public List<string> fields;

            public ReflectedData(string name, List<string> fields) {
                this.name = name;
                this.fields = fields;
            }
        }

        private struct VarData {
            public string name;
            public int offset;
            public int size;
            public int align;

            public VarData(string name, int offset, int size, int align) {
                this.name = name;
                this.offset = offset;
                this.size = size;
                this.align = align;
            }
        }

        private class StructureDict : Dictionary<string, VariableDict>, IStructReflector {
            public int GetOffset(string structName, string varName) {
                try {
                    return this[structName][varName];
                } catch(Exception e) {
                    Console.WriteLine(structName + " " + varName + " " + e.ToString());
                    throw e;
                }
            }
            public int GetSelfAlignedSize(string structName) => this[structName].SelftAlignedSize;
        }

        private class VariableDict : Dictionary<string, int> {
            public int Alignment { get; private set; }
            public int Size { get; private set; }
            public int SelftAlignedSize => AlignByte(Size, Alignment);

            public VariableDict(int size = 0) => Size = size;

            public void Add(VarData data) {
                if(data.offset == 0) {
                    Alignment = data.align;
                }
                int totSize = data.offset + data.size;
                if(totSize > Size) {
                    Size = totSize;
                }
                Add(data.name, data.offset);
            }
        }
    }

    public interface IStructReflector {
        int GetOffset(string structName, string varName);
        int GetSelfAlignedSize(string structName);
    }
}