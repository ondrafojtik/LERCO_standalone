using System.Collections.Generic;
using UnityEngine;

// Both app instances run the same scene, so an object is identified by where it sits in the hierarchy.
public static class MultiuserIds
{
    public static uint Hash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            for (int i = 0; i < s.Length; i++)
            {
                h ^= s[i];
                h *= 16777619;
            }
            return h;
        }
    }

    public static ulong Hash(byte[] bytes)
    {
        unchecked
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < bytes.Length; i++)
            {
                h ^= bytes[i];
                h *= 1099511628211UL;
            }
            return h;
        }
    }

    // "Root/Child[0]/Child[2]": the number is the position among siblings with the same name,
    // so objects created at runtime next to it do not change the path.
    public static string PathOf(Transform t)
    {
        List<string> parts = new List<string>();
        while (t != null)
        {
            parts.Add(t.parent == null ? t.name : t.name + "[" + SameNameIndex(t) + "]");
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    static int SameNameIndex(Transform t)
    {
        Transform parent = t.parent;
        int siblingIndex = t.GetSiblingIndex();
        int index = 0;
        for (int i = 0; i < siblingIndex; i++)
        {
            if (parent.GetChild(i).name == t.name) index++;
        }
        return index;
    }
}
