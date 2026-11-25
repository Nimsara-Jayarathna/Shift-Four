using UnityEngine;

namespace ShiftFour
{
    // The shared signage shader depth-tests labels. Each label binds its font's dynamic atlas.
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(TextMesh))]
    public sealed class PrototypeLabel : MonoBehaviour
    {
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private TextMesh label;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock properties;

        private void OnEnable()
        {
            label = GetComponent<TextMesh>();
            meshRenderer = GetComponent<MeshRenderer>();
            if (label.font == null || meshRenderer == null) return;
            Font.textureRebuilt += BindFontAtlas;
            label.font.RequestCharactersInTexture(label.text, label.fontSize, label.fontStyle);
            BindFontAtlas(label.font);
        }

        private void OnDisable() => Font.textureRebuilt -= BindFontAtlas;

        private void BindFontAtlas(Font font)
        {
            if (label == null || font != label.font || meshRenderer == null) return;
            Texture atlas = font.material.mainTexture;
            if (atlas == null) return;
            properties ??= new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMap, atlas);
            meshRenderer.SetPropertyBlock(properties);
        }
    }
}
