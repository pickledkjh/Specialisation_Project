using UnityEngine;

/// <summary>
/// Placeholder guard visual shared by the player and the AI: a translucent blue
/// energy panel floating in front of the mech while the shield is up. Runtime-built,
/// no assets needed. Swap for a proper shield model/VFX later by replacing Create().
/// </summary>
public static class ShieldVisual
{
    public static GameObject Create(Transform owner)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(quad.GetComponent<Collider>()); // visual only - blocking is resolved in code
        quad.name = "Shield";
        quad.transform.SetParent(owner, false);
        quad.transform.localPosition = new Vector3(0f, 1.3f, 1.1f);
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(2.4f, 2.8f, 1f);

        Renderer r = quad.GetComponent<Renderer>();
        Shader s = Shader.Find("Sprites/Default"); // double-sided + transparent, works in URP
        if (s != null && r != null)
        {
            r.material = new Material(s);
            r.material.color = new Color(0.35f, 0.75f, 1f, 0.35f);
        }
        return quad;
    }
}
