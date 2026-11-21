using UnityEngine;
using UnityEngine.Rendering;

namespace ShiftFour
{
    public static class PrototypeGeometry
    {
        public static Transform Group(string name, Transform parent, Vector3 position = default)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            group.localPosition = position;
            return group;
        }

        public static GameObject Box(string name, Transform parent, Vector3 position,
            Vector3 size, Material material, bool collision = true)
            => Shape(name, PrimitiveType.Cube, parent, position, size, material, collision);

        public static GameObject Cylinder(string name, Transform parent, Vector3 position,
            float diameter, float height, Material material, bool collision = true)
            => Shape(name, PrimitiveType.Cylinder, parent, position,
                new Vector3(diameter, height * .5f, diameter), material, collision);

        public static GameObject Sphere(string name, Transform parent, Vector3 position,
            Vector3 size, Material material, bool collision = true)
            => Shape(name, PrimitiveType.Sphere, parent, position, size, material, collision);

        private static GameObject Shape(string name, PrimitiveType type, Transform parent,
            Vector3 position, Vector3 size, Material material, bool collision)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = size;
            item.GetComponent<Renderer>().sharedMaterial = material;
            item.GetComponent<Collider>().enabled = collision;
            return item;
        }

        public static TextMesh Label(string text, Transform parent, Vector3 position,
            float characterSize, Font font, float maxWidth, float maxHeight, Material lettering)
        {
            var label = Group(text, parent, position).gameObject.AddComponent<TextMesh>();
            label.text = text;
            label.font = font;
            label.fontSize = 64;
            label.characterSize = characterSize;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = new Color(.9f, .95f, .94f);
            var renderer = label.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            font.RequestCharactersInTexture(text, label.fontSize);
            Vector3 bounds = renderer.localBounds.size;
            float scale = 1;
            if (maxWidth > 0 && bounds.x > maxWidth) scale = maxWidth / bounds.x;
            if (maxHeight > 0 && bounds.y > maxHeight) scale = Mathf.Min(scale, maxHeight / bounds.y);
            label.transform.localScale = Vector3.one * scale;
            UseDepthTestedLettering(label, lettering);
            return label;
        }

        public static void UseDepthTestedLettering(TextMesh label, Material lettering)
        {
            if (lettering == null) return;
            label.GetComponent<MeshRenderer>().sharedMaterial = lettering;
            label.gameObject.AddComponent<PrototypeLabel>();
        }
    }
}
