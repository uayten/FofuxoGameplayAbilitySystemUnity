using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The "Add" behind a missing section of the actor panel. It offers the
/// concrete types a slot can hold — for attributes, the project's own
/// <c>AttributeSet</c> subclasses and never the base — and adds the one picked
/// as a plain component: visible in the Inspector, undoable, no hide flags.
///
/// <c>[RequireComponent(typeof(AttributeSet))]</c> would have been the wrong
/// tool for the same job: it adds the base class, and an actor that later gets
/// its real set ends up with two, with <c>GetComponent&lt;AttributeSet&gt;()</c>
/// returning whichever Unity finds first.
/// </summary>
internal static class AbilitySystemComponentMenu
{
    private static readonly Dictionary<System.Type, List<System.Type>> cache = new();

    /// <summary>
    /// The types an Add may create for a slot, sorted by name: the addable
    /// subclasses when the project has any, otherwise the slot type itself.
    /// Cached per slot for the life of the domain; a recompile rebuilds it.
    /// </summary>
    public static List<System.Type> ConcreteTypes(System.Type slotType)
    {
        if (!cache.TryGetValue(slotType, out List<System.Type> types))
        {
            types = ConcreteTypes(slotType, TypeCache.GetTypesDerivedFrom(slotType));
            cache[slotType] = types;
        }

        return types;
    }

    /// <summary>
    /// The filter on its own, over any candidate list, so a test can feed it
    /// types without registering them with the Editor.
    /// </summary>
    internal static List<System.Type> ConcreteTypes(
        System.Type slotType, IEnumerable<System.Type> derived)
    {
        List<System.Type> result = new();
        foreach (System.Type type in derived)
        {
            if (type != slotType && IsAddable(type))
            {
                result.Add(type);
            }
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        if (result.Count == 0 && IsAddable(slotType))
        {
            result.Add(slotType);
        }

        return result;
    }

    /// <summary>
    /// A type the Add Component menu itself would accept: concrete, closed, a
    /// component, and declared at file scope. A nested MonoBehaviour cannot
    /// back a script asset, and a test fixture's private subclass must never
    /// appear in a project's Inspector.
    /// </summary>
    internal static bool IsAddable(System.Type type)
    {
        return type != null &&
               !type.IsAbstract &&
               !type.IsGenericTypeDefinition &&
               !type.IsNested &&
               typeof(Component).IsAssignableFrom(type);
    }

    /// <summary>
    /// Adds the component with Undo, exactly as the Add Component menu would.
    /// The Inspector keeps showing it: the panel reads the sibling, it does not
    /// replace it.
    /// </summary>
    public static Component Add(GameObject owner, System.Type type)
    {
        if (owner == null || type == null || !typeof(Component).IsAssignableFrom(type))
        {
            return null;
        }

        return Undo.AddComponent(owner, type);
    }
}
