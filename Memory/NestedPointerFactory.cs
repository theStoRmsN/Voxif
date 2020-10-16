using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LiveSplit.VoxSplitter {
    public class NestedPointerFactory {

        protected Memory memory;

        protected EDerefType derefType;

        protected Dictionary<Pointer, HashSet<NodePointer>> nodeLink = new Dictionary<Pointer, HashSet<NodePointer>>();

        public NestedPointerFactory(Memory memory) : this(memory, EDerefType.Auto) { }

        public NestedPointerFactory(Memory memory, EDerefType derefType) {
            this.memory = memory;
            this.derefType = derefType;
        }

        protected HashSet<Pointer> BasePointers() => nodeLink.Keys.Where(n => n is BasePointer).ToHashSet();

        public BasePointer MakeBase(IntPtr basePtr) {
            return MakeBase(derefType, basePtr);
        }

        public BasePointer MakeBase(EDerefType derefType, IntPtr basePtr) {
            BasePointer pointer = (BasePointer)Make(typeof(IntPtr), derefType, basePtr, null);
            _ = pointer.New;
            return pointer;
        }

        public StructPointer<T> Make<T>(IntPtr basePtr, int offset, params int[] offsets) where T : unmanaged {
            return Make<T>(derefType, basePtr, offset, offsets);
        }

        public StructPointer<T> Make<T>(EDerefType derefType, IntPtr basePtr, int offset, params int[] offsets) where T : unmanaged {
            StructPointer<T> pointer = (StructPointer<T>)Make(typeof(T), derefType, basePtr, offsets.Prepend(offset).ToArray());
            _ = pointer.New;
            return pointer;
        }

        public StructPointer<T> Make<T>(Pointer parent, int offset, params int[] offsets) where T : unmanaged {
            return Make<T>(derefType, parent, offset, offsets);
        }

        public StructPointer<T> Make<T>(EDerefType derefType, Pointer parent, int offset, params int[] offsets) where T : unmanaged {
            StructPointer<T> pointer = (StructPointer<T>)Make(typeof(T), derefType, parent, offsets.Prepend(offset).ToArray());
            _ = pointer.New;
            return pointer;
        }

        public StringPointer MakeString(IntPtr basePtr, int offset, params int[] offsets) {
            return MakeString(derefType, basePtr, offset, offsets);
        }

        public StringPointer MakeString(EDerefType derefType, IntPtr basePtr, int offset, params int[] offsets) {
            StringPointer pointer = (StringPointer)Make(typeof(string), derefType, basePtr, offsets.Prepend(offset).ToArray());
            _ = pointer.New;
            return pointer;
        }

        public StringPointer MakeString(Pointer parent, int offset, params int[] offsets) {
            return MakeString(derefType, parent, offset, offsets);
        }

        public StringPointer MakeString(EDerefType derefType, Pointer parent, int offset, params int[] offsets) {
            StringPointer pointer = (StringPointer)Make(typeof(string), derefType, parent, offsets.Prepend(offset).ToArray());
            _ = pointer.New;
            return pointer;
        }

        protected Pointer Make(Type type, EDerefType derefType, IntPtr basePtr, params int[] offsets) {
            Pointer pointer;
            foreach(BasePointer basePointer in BasePointers()) {
                if(basePointer.Base != basePtr) {
                    continue;
                }

                if(offsets == null) {
                    return basePointer;
                }

                pointer = AddPointer(type, nodeLink[basePointer], derefType, offsets);

                if(pointer == null) {
                    pointer = CreateStructOrString(type, basePointer, derefType, offsets);
                    nodeLink[basePointer].Add((NodePointer)pointer);
                }
                return pointer;
            }
            BasePointer newBasePointer = new BasePointer(memory, basePtr, derefType);
            if(offsets != null) {
                pointer = CreateStructOrString(type, newBasePointer, derefType, offsets);
                nodeLink.Add(newBasePointer, new HashSet<NodePointer> { (NodePointer)pointer });
                return pointer;
            } else {
                nodeLink.Add(newBasePointer, new HashSet<NodePointer>());
                return newBasePointer;
            }
        }

        protected Pointer Make(Type type, EDerefType derefType, Pointer parent, params int[] offsets) {
            Pointer pointer = null;
            if(nodeLink.TryGetValue(parent, out HashSet<NodePointer> value)) {
                pointer = AddPointer(type, value, derefType, offsets);
            } else {
                nodeLink.Add(parent, new HashSet<NodePointer>());
            }
            if(pointer == null) {
                pointer = CreateStructOrString(type, parent, derefType, offsets);
                nodeLink[parent].Add((NodePointer)pointer);
            }
            return pointer;
        }

        private NodePointer AddPointer(Type type, HashSet<NodePointer> pointers, EDerefType derefType, int[] offsets) {
            NodePointer newNode = null;
            foreach(NodePointer ptr in pointers) {
                if(ptr.offsets[0] != offsets[0]) {
                    continue;
                }

                if(ptr.offsets.SequenceEqual(offsets)) {                        
                    return ptr;
                }

                for(int o = 1; o < ptr.offsets.Length; o++) {
                    if(ptr.offsets[o] == offsets[o]) {
                        continue;
                    }
                    NodePointer newParent = new StructPointer<IntPtr>(memory, ptr.parent, derefType, ptr.offsets.Take(o).ToArray());
                    pointers.Remove(ptr);
                    pointers.Add(newParent);
                    ptr.parent = newParent;
                    ptr.offsets = ptr.offsets.Skip(o).ToArray();
                    if(o != offsets.Length) {
                        newNode = CreateStructOrString(type, newParent, derefType, offsets.Skip(o).ToArray());
                        nodeLink.Add(newParent, new HashSet<NodePointer> { ptr, newNode });
                        return newNode;
                    } else {
                        nodeLink.Add(newParent, new HashSet<NodePointer> { ptr });
                        return newParent;
                    }
                }

                if(nodeLink.ContainsKey(ptr) && (newNode = AddPointer(type, nodeLink[ptr], derefType, offsets.Skip(ptr.offsets.Length).ToArray())) != null) {
                    return newNode;
                }

                newNode = CreateStructOrString(type, ptr, derefType, offsets.Skip(ptr.offsets.Length).ToArray());
                if(nodeLink.ContainsKey(ptr)) {
                    nodeLink[ptr].Add(newNode);
                } else {
                    nodeLink[ptr] = new HashSet<NodePointer> { newNode };
                }
            }
            return newNode;
        }

        private NodePointer CreateStructOrString(Type type, Pointer basePointer, EDerefType derefType, params int[] offsets) {
            return type == typeof(string)
                 ? new StringPointer(memory, basePointer, derefType, offsets)
                 : (NodePointer)Activator.CreateInstance(typeof(StructPointer<>).MakeGenericType(type), new object[] { memory, basePointer, derefType, offsets });
        }

        public override string ToString() {
            string str = "";
            foreach(BasePointer baseNode in BasePointers()) {
                str += Environment.NewLine + "0x" + baseNode.Base.ToString("X8");
                foreach(NodePointer node in nodeLink[baseNode]) {
                    str += NodeToString(node);
                }
            }
            return str;
        }

        private string NodeToString(NodePointer pointer) {
            string str = Environment.NewLine;
            str += new string(' ', pointer.GetTreeDepth() * 4) + pointer.OffsetsToString();

            if(nodeLink.ContainsKey(pointer)) {
                foreach(NodePointer subNode in nodeLink[pointer]) {
                    str += NodeToString(subNode);
                }
            }
            return str;
        }
    }

    public enum EDerefType { Auto, Bit32, Bit64 };

    public abstract class Pointer {

        protected Memory memory;

        protected uint lastTickUpdate = 0;

        protected object oldValue;
        public object Old {
            get {
                if(lastTickUpdate != memory.Tick) {
                    lastTickUpdate = memory.Tick;
                    Update();
                }
                return oldValue;
            }
            set => oldValue = value;
        }

        protected object newValue;
        public object New {
            get {
                if(lastTickUpdate != memory.Tick) {
                    lastTickUpdate = memory.Tick;
                    Update();
                }
                return newValue;
            }
            set => newValue = value;
        }

        public bool Changed => !Old.Equals(newValue);

        public EDerefType derefType;

        public Pointer(Memory memory, EDerefType derefType) {
            this.memory = memory;
            this.derefType = derefType;
        }

        protected abstract void Update();
    }

    public class BasePointer : Pointer {

        public new IntPtr Old {
            get => (IntPtr)base.Old;
            set => base.Old = value;
        }
        public new IntPtr New {
            get => (IntPtr)base.New;
            set => base.New = value;
        }

        public IntPtr Base { get; protected set; }

        public BasePointer(Memory memory, IntPtr basePtr) : this(memory, basePtr, EDerefType.Auto) { }
        public BasePointer(Memory memory, IntPtr basePtr, EDerefType derefType) : base(memory, derefType) {
            oldValue = newValue = Base = basePtr;
        }

        protected override void Update() { }
    }

    public abstract class NodePointer : Pointer {
        public int[] offsets;
        public Pointer parent;

        public NodePointer(Memory memory, Pointer parent, EDerefType derefType, params int[] offsets) : base(memory, derefType) {
            this.offsets = offsets;
            this.parent = parent;
        }

        protected IntPtr DerefOffsets() {
            return memory.game.DerefOffsets(derefType, (IntPtr)parent.New, offsets);
        }

        public int GetTreeDepth() => parent is NodePointer nodeParent ? 1 + nodeParent.GetTreeDepth() : 1;

        public string OffsetsToString() {
            if((offsets?.Length ?? 0) == 0) {
                return "";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("[0x").Append(offsets[0].ToString("X"));

            for(int i = 1; i < offsets.Length; i++) {
                sb.Append(", 0x").Append(offsets[i].ToString("X"));
            }

            return sb.Append(']').ToString();
        }
    }

    public class StructPointer<T> : NodePointer where T : unmanaged {

        public new T Old {
            get => (T)base.Old;
            set => base.Old = value;
        }
        public new T New {
            get => (T)base.New;
            set => base.New = value;
        }

        public StructPointer(Memory memory, Pointer parent, params int[] offsets) : this(memory, parent, EDerefType.Auto, offsets) { }
        public StructPointer(Memory memory, Pointer parent, EDerefType derefType, params int[] offsets) : base(memory, parent, derefType, offsets) { }

        protected override void Update() {
            Old = (T)(newValue ?? default(T));
            New = Deref();
        }

        protected T Deref() => memory.game.Read<T>(DerefOffsets(), derefType);
    }

    public enum EStringType { Auto, UTF8, UTF8Sized, UTF16, UTF16Sized }

    public class StringPointer : NodePointer {

        public new string Old {
            get => (string)base.Old;
            set => base.Old = value;
        }
        public new string New {
            get => (string)base.New;
            set => base.New = value;
        }

        public EStringType StringType { get; set; }

        public StringPointer(Memory memory, Pointer parent, params int[] offsets) : this(memory, parent, EDerefType.Auto, offsets) { }
        public StringPointer(Memory memory, Pointer parent, EDerefType derefType, params int[] offsets) : base(memory, parent, derefType, offsets) { }

        protected override void Update() {
            Old = (string)newValue ?? "";
            New = Deref();
        }

        protected string Deref() => memory.game.ReadString(DerefOffsets(), StringType);
    }
}