using System.Collections.Generic;
using UnityEngine;

namespace MMONetworking.Client
{
public static class UnityMmoMaterialFactory
{
    private static readonly Dictionary<Color32, Material> MaterialsByColor = new Dictionary<Color32, Material>();
    private static readonly string[] ShaderCandidates =
    {
        "Universal Render Pipeline/Lit",
        "Universal Render Pipeline/Simple Lit",
        "HDRP/Lit",
        "Standard",
        "Legacy Shaders/Diffuse",
        "Unlit/Color"
    };

    public static Material Create(Color color)
    {
        var key = (Color32)color;
        Material existing;
        if (MaterialsByColor.TryGetValue(key, out existing) && existing != null)
        {
            return existing;
        }

        Shader shader = null;
        for (var i = 0; i < ShaderCandidates.Length; i++)
        {
            shader = Shader.Find(ShaderCandidates[i]);
            if (shader != null)
            {
                break;
            }
        }

        if (shader == null)
        {
            Debug.LogWarning("No compatible shader found for runtime MMO material creation.");
            return null;
        }

        var material = new Material(shader);
        ApplyColor(material, color);
        MaterialsByColor[key] = material;
        return material;
    }

    public static void Apply(Renderer renderer, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        var material = Create(color);
        if (material != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    private static void ApplyColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }
}
}
