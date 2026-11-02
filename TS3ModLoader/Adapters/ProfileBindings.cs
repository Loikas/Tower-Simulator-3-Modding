using System;
using System.Reflection;
using UnityEngine;

namespace TS3Modding.Adapters
{
    internal static class ProfileBindings
    {
        public static Type ComponentType(Assembly assembly, string name)
        {
            var type = assembly.GetType(name, true, false);
            if (!typeof(MonoBehaviour).IsAssignableFrom(type)) throw new NotSupportedException("Invalid component type: " + name);
            return type;
        }
        public static FieldInfo Field(Type owner, string name, Type expected)
        {
            var field = owner.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != expected) throw new NotSupportedException("Incompatible field: " + owner.FullName + "." + name);
            return field;
        }
        public static bool InLoadedScene(Component component) =>
            component && component.gameObject.scene.IsValid() && component.gameObject.scene.isLoaded;
    }
}
