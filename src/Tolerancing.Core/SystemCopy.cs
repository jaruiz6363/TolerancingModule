using System.Collections;
using System.Reflection;
using AberrationCalculator.Core.Models;

namespace Tolerancing.Core;

/// <summary>
/// Deep copies of AberrationCalculator's lens model, so that every perturbed lens is a lens of
/// its own and perturbing one never touches the nominal or another.
///
/// <para>The copy is made field by field, by reflection, rather than by naming each property,
/// so a field AberrationCalculator adds later is copied too instead of being shared by mistake.
/// It refuses any type it does not know how to copy rather than sharing it.</para>
/// </summary>
public static class SystemCopy
{
    private static readonly MethodInfo Memberwise =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly Assembly Model = typeof(OpticalSystem).Assembly;

    public static OpticalSystem Of(OpticalSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        return (OpticalSystem)Copy(system, new Dictionary<object, object>(ReferenceEqualityComparer.Instance))!;
    }

    private static object? Copy(object? value, Dictionary<object, object> copied)
    {
        if (value == null || value is string) return value;
        var type = value.GetType();
        if (type.IsValueType) return value;
        if (copied.TryGetValue(value, out var done)) return done;

        if (value is Array array)
        {
            var copy = (Array)array.Clone();
            copied[value] = copy;
            if (!type.GetElementType()!.IsValueType)
                for (int i = 0; i < copy.Length; i++) copy.SetValue(Copy(array.GetValue(i), copied), i);
            return copy;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = (IList)value;
            var copy = (IList)Activator.CreateInstance(type, list.Count)!;
            copied[value] = copy;
            foreach (var item in list) copy.Add(Copy(item, copied));
            return copy;
        }

        if (type.Assembly != Model || typeof(Delegate).IsAssignableFrom(type))
            throw new NotSupportedException(
                $"{type.FullName} in the lens model cannot be copied; SystemCopy needs to learn it");

        var clone = Memberwise.Invoke(value, null)!;
        copied[value] = clone;
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                          | BindingFlags.DeclaredOnly))
            {
                if (!f.FieldType.IsValueType)
                    f.SetValue(clone, Copy(f.GetValue(value), copied));
                else if (HoldsReferences(f.FieldType))
                    // Copied by value, a struct's references would be shared.
                    throw new NotSupportedException(
                        $"{type.FullName}.{f.Name} is a struct holding references; SystemCopy needs to learn it");
            }
        return clone;
    }

    private static bool HoldsReferences(Type type)
    {
        if (!type.IsValueType) return true;
        if (type.IsPrimitive || type.IsEnum) return false;
        return type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                   .Any(f => HoldsReferences(f.FieldType));
    }
}
