using System;
using System.Collections.Generic;
using System.Linq;
using Voxif.Memory;

namespace Voxif.Helpers.Unity {
    public class MonoNestedPointerFactory : NestedPointerFactory {

        protected IMonoHelper mono;

        public MonoNestedPointerFactory(TickableProcessWrapper wrapper, IMonoHelper monoHelper)
            : this(wrapper, null, monoHelper, EDerefType.Auto) { }
        public MonoNestedPointerFactory(TickableProcessWrapper wrapper, string moduleName, IMonoHelper monoHelper)
            : this(wrapper, moduleName, monoHelper, EDerefType.Auto) { }
        public MonoNestedPointerFactory(TickableProcessWrapper wrapper, string moduleName, IMonoHelper monoHelper, EDerefType derefType)
            : base(wrapper, moduleName, derefType) {
            mono = monoHelper;
        }


        public Pointer<T> Make<T>(string klassName, string instanceName, params int[] offsets) where T : unmanaged {
            return Make<T>(mono.MainImage, klassName, instanceName, out _, offsets);
        }
        public Pointer<T> Make<T>(string klassName, string instanceName, out IntPtr klass, params int[] offsets) where T : unmanaged {
            return Make<T>(mono.MainImage, klassName, instanceName, out klass, offsets);
        }
        public Pointer<T> Make<T>(IntPtr image, string klassName, string instanceName, params int[] offsets) where T : unmanaged {
            return Make<T>(image, klassName, instanceName, out _, offsets);
        }
        public Pointer<T> Make<T>(IntPtr image, string klassName, string instanceName, out IntPtr klass, params int[] offsets) where T : unmanaged {
            return (Pointer<T>)Make(typeof(T), image, klassName, instanceName, out klass, offsets);
        }

        public Pointer<T> Make<T>(string klassName, string instanceName, string fieldName, params int[] offsets) where T : unmanaged {
            return Make<T>(mono.MainImage, klassName, instanceName, fieldName, offsets);
        }
        public Pointer<T> Make<T>(IntPtr image, string klassName, string instanceName, string fieldName, params int[] offsets) where T : unmanaged {
            return (Pointer<T>)Make(typeof(T), image, klassName, instanceName, fieldName, offsets);
        }


        public StringPointer MakeString(string klassName, string instanceName, params int[] offsets) {
            return MakeString(mono.MainImage, klassName, instanceName, out _, offsets);
        }
        public StringPointer MakeString(string klassName, string instanceName, out IntPtr klass, params int[] offsets) {
            return MakeString(mono.MainImage, klassName, instanceName, out klass, offsets);
        }
        public StringPointer MakeString(IntPtr image, string klassName, string instanceName, params int[] offsets) {
            return MakeString(image, klassName, instanceName, out _, offsets);
        }
        public StringPointer MakeString(IntPtr image, string klassName, string instanceName, out IntPtr klass, params int[] offsets) {
            return (StringPointer)Make(typeof(string), image, klassName, instanceName, out klass, offsets);
        }

        public StringPointer MakeString(string klassName, string instanceName, string fieldName, params int[] offsets) {
            return MakeString(mono.MainImage, klassName, instanceName, fieldName, offsets);
        }
        public StringPointer MakeString(IntPtr image, string klassName, string instanceName, string fieldName, params int[] offsets) {
            return (StringPointer)Make(typeof(string), image, klassName, instanceName, fieldName, offsets);
        }


        public Pointer Make(Type type, IntPtr image, string klassName, string instanceName, out IntPtr klass, params int[] offsets) {
            IntPtr staticBase = mono.GetStaticField(image, klassName, instanceName, out klass, out int instanceOffset);
            return CreateBase(type, staticBase, offsets.Prepend(instanceOffset).ToArray());
        }
        public Pointer Make(Type type, IntPtr image, string klassName, string instanceName, string fieldName, params int[] offsets) {
            IntPtr staticBase = mono.GetStaticField(image, klassName, instanceName, out IntPtr klass, out int instanceOffset);
            return CreateBase(type, staticBase, offsets.Prepend(mono.GetFieldOffset(klass, fieldName)).Prepend(instanceOffset).ToArray());
        }
        protected Pointer CreateBase(Type type, IntPtr ptr, params int[] offsets) {
            MonoBasePointer monoBase = new MonoBasePointer(wrapper, mono, ptr);
            monoBase.ForceUpdate();
            Pointer pointer = (Pointer)CreateNodeStructOrString(type, monoBase, defaultDerefType, offsets);
            pointer.ForceUpdate();
            nodeLink.Add(monoBase, new HashSet<IPointer> { pointer });
            return pointer;
        }
    }

    public class MonoBasePointer : BasePointer<IntPtr> {
        protected IMonoHelper mono;

        public MonoBasePointer(TickableProcessWrapper wrapper, IMonoHelper mono, IntPtr basePtr)
            : this(wrapper, mono, basePtr, EDerefType.Auto) { }
        public MonoBasePointer(TickableProcessWrapper wrapper, IMonoHelper mono, IntPtr basePtr, EDerefType derefType)
            : base(wrapper, basePtr, derefType) {
            this.mono = mono;
        }

        protected override void Update() {
            Old = (IntPtr)(newValue ?? default(IntPtr));
            New = mono.GetStaticAddress(Base);
        }

        protected override IntPtr DerefOffsets() => throw new NotImplementedException();
    }
}