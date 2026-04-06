using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
public static class JsonUtilityWrapper
{
    public static T FromJson<T>(string json) where T : class
    {
        var value = JsonUtility.FromJson<T>(json);
        if (value == null)
        {
            throw new System.InvalidOperationException("Failed to parse JSON into " + typeof(T).Name + ".");
        }

        return value;
    }

    public static string ToJson<T>(T value, bool prettyPrint)
        => JsonUtility.ToJson(value, prettyPrint);
}
}
