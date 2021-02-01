using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;

namespace Voxif.AutoSplitter {
    public static class ExtensionMethods {
        //
        // XML
        //
        public static XmlElement ToElement<T>(this XmlDocument document, string name, T value) {
            XmlElement xmlElement = document.CreateElement(name);
            xmlElement.InnerText = value.ToString();
            return xmlElement;
        }

        //
        // ENUM
        //
        public static string GetDescription(this Enum enumVal) {
            return enumVal.GetAttributeOfType<DescriptionAttribute>()?.Description;
        }
        public static Type GetType(this Enum enumVal) {
            return enumVal.GetAttributeOfType<TypeAttribute>()?.Type;
        }
        public static T GetAttributeOfType<T>(this Enum enumVal) where T : Attribute {
            Type type = enumVal.GetType();
            object[] attributes = type.GetMember(Enum.GetName(type, enumVal))[0].GetCustomAttributes(typeof(T), false);
            return (attributes.Length > 0) ? (T)attributes[0] : null;
        }


        //
        // ASSEMBLY
        //
        public static string FullComponentName(this Assembly asm) {
            string name = asm.GetName().Name.Substring(10);
            StringBuilder sb = new StringBuilder(name.Length * 2);
            sb.Append(name[0]);
            for(int i = 1; i < name.Length; i++) {
                if(Char.IsUpper(name[i]) && name[i - 1] != ' ') {
                    sb.Append(' ');
                }
                sb.Append(name[i]);
            }
            sb.Append(" Autosplitter v").Append(asm.GetName().Version.ToString(3));
            return sb.ToString();
        }
        public static string GitMainURL(this Assembly asm) => Path.Combine("https://raw.githubusercontent.com/Voxelse", asm.GetName().Name, "main/");
        public static string ResourcesURL(this Assembly asm) => Path.Combine(asm.GitMainURL(), "Resources");
        public static string ResourcesPath(this Assembly asm) => Path.Combine(Path.GetDirectoryName(asm.Location), asm.GetName().Name);
        public static string Description(this Assembly asm) => ((AssemblyDescriptionAttribute)Attribute.GetCustomAttribute(asm, typeof(AssemblyDescriptionAttribute))).Description;


        //
        // ARRAY
        //
        // Linq.Prepend replacement function for .net framework 4.6.1
        public static T[] Prepend<T>(this T[] array, T value) {
            T[] newArray = new T[array.Length + 1];
            newArray[0] = value;
            Array.Copy(array, 0, newArray, 1, array.Length);
            return newArray;
        }
    }
}