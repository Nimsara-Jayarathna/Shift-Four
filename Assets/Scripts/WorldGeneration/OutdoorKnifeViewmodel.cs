using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    [DisallowMultipleComponent]
    internal sealed class OutdoorKnifeViewmodel : MonoBehaviour
    {
        private static readonly Vector3 RestPosition = new Vector3(.205f, -.16f, .53f);
        private static readonly Quaternion RestAngle = Quaternion.Euler(8f, -14f, -8f);
        private const float SwingDuration = .26f;
        private const int ViewmodelLayer = 2; // Existing weapon presentation / Ignore Raycast layer.

        private Camera localCamera;
        private Renderer[] renderers;
        private Mesh bladeMesh, rightArmMesh;

        internal static OutdoorKnifeViewmodel Build(Camera camera, PrototypeMaterials materials)
        {
            Transform root = Group("First Person Knife", camera.transform, RestPosition);
            root.gameObject.SetActive(false);
            root.localRotation = RestAngle;
            var model = root.gameObject.AddComponent<OutdoorKnifeViewmodel>();
            model.localCamera = camera;

            Transform gripAnchor = Group("Knife grip anchor", root, new Vector3(.013f, -.045f, -.044f));
            gripAnchor.localRotation = Quaternion.Euler(19f, 0f, 3f);
            model.BuildGrip(gripAnchor, materials);
            model.BuildKnife(gripAnchor, materials);

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = ViewmodelLayer;
            model.renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in model.renderers)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.forceRenderingOff = true;
            }
            return model;
        }

        internal void Present(bool visible, float swingStarted)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible) return;

            float elapsed = Time.time - swingStarted;
            float sweep = elapsed < SwingDuration ? Mathf.Sin(Mathf.PI * Mathf.Clamp01(elapsed / SwingDuration)) : 0f;
            float breath = Mathf.Sin(Time.time * 1.7f);
            transform.localPosition = RestPosition + Vector3.up * (.002f * breath) +
                new Vector3(-.19f, .041f, .038f) * sweep;
            transform.localRotation = RestAngle * Quaternion.Euler(-12f * sweep, -27f * sweep, 46f * sweep);
        }

        private void BuildGrip(Transform anchor, PrototypeMaterials materials)
        {
            var prefab = Resources.Load<GameObject>("Weapons/FirstPersonArmsRig");
            var instance = prefab != null ? Instantiate(prefab, transform, false) : null;
            var rig = instance != null ? instance.GetComponent<FirstPersonArmsRig>() : null;
            var pose = instance != null ? instance.GetComponent<FirstPersonPistolGrip>() : null;
            if (rig != null && rig.IsValid && rig.Skin.sharedMesh.isReadable && pose != null && pose.Initialize())
            {
                instance.name = "Knife Right Arm Rig";
                pose.ApplyPose(0f, null, Vector3.zero);
                pose.enabled = false;
                CurlIndexFinger(rig.RightHand);
                rightArmMesh = BuildRightArmMesh(rig.Skin, rig.RightUpperArm);
                rig.Skin.sharedMesh = rightArmMesh;
                rig.Skin.updateWhenOffscreen = true;
                anchor.SetParent(rig.RightHand, true);
                return;
            }

            if (instance != null)
            {
                instance.SetActive(false);
                instance.transform.SetParent(null, false);
                Destroy(instance);
            }
            Debug.LogWarning("Outdoor knife viewmodel could not bind the tactical arm rig; using the temporary glove.", this);
            BuildTemporaryGlove(anchor, materials);
        }

        private static void CurlIndexFinger(Transform hand)
        {
            Transform proximal = null, middle = null, distal = null;
            foreach (Transform bone in hand.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "f_index.01.R") proximal = bone;
                else if (bone.name == "f_index.02.R") middle = bone;
                else if (bone.name == "f_index.03.R") distal = bone;
            }
            if (proximal == null || middle == null || distal == null) return;

            // Curl the pistol trigger finger toward the same grip as the three already curled fingers.
            proximal.rotation = Quaternion.FromToRotation(middle.position - proximal.position,
                hand.TransformDirection(new Vector3(.04f, .96f, -.30f))) * proximal.rotation;
            middle.rotation = Quaternion.FromToRotation(distal.position - middle.position,
                hand.TransformDirection(new Vector3(-.13f, .24f, -.96f))) * middle.rotation;
        }

        private static Mesh BuildRightArmMesh(SkinnedMeshRenderer skin, Transform upperArm)
        {
            Mesh source = skin.sharedMesh;
            Mesh mesh = Instantiate(source);
            mesh.name = "Outdoor knife right arm";
            Transform[] bones = skin.bones;
            var rightBone = new bool[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                rightBone[i] = bones[i] != null && bones[i].IsChildOf(upperArm);

            BoneWeight[] weights = source.boneWeights;
            var rightVertex = new bool[source.vertexCount];
            for (int i = 0; i < weights.Length; i++)
            {
                BoneWeight weight = weights[i];
                float right = (rightBone[weight.boneIndex0] ? weight.weight0 : 0f) +
                    (rightBone[weight.boneIndex1] ? weight.weight1 : 0f) +
                    (rightBone[weight.boneIndex2] ? weight.weight2 : 0f) +
                    (rightBone[weight.boneIndex3] ? weight.weight3 : 0f);
                rightVertex[i] = right > .5f;
            }

            // The source contains both arms in one skin. Keep the original rig, weights and materials.
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                int[] indices = source.GetTriangles(submesh);
                var kept = new List<int>(indices.Length / 2);
                for (int i = 0; i < indices.Length; i += 3)
                    if (rightVertex[indices[i]] || rightVertex[indices[i + 1]] || rightVertex[indices[i + 2]])
                    {
                        kept.Add(indices[i]);
                        kept.Add(indices[i + 1]);
                        kept.Add(indices[i + 2]);
                    }
                mesh.SetTriangles(kept, submesh, false);
            }
            return mesh;
        }

        private void BuildKnife(Transform grip, PrototypeMaterials materials)
        {
            Box("Knife handle", grip, Vector3.zero, new Vector3(.035f, .10f, .046f), materials.Metal, false);
            Box("Knife pommel", grip, Vector3.down * .05f, new Vector3(.043f, .016f, .05f), materials.Concrete, false);
            Box("Knife guard", grip, Vector3.up * .064f, new Vector3(.084f, .014f, .052f), materials.Checkpoint, false);

            Transform steel = Group("Steel blade", grip);
            bladeMesh = new Mesh { name = "Outdoor knife blade" };
            bladeMesh.vertices = new[]
            {
                new Vector3(-.033f, .07f, -.008f), new Vector3(.031f, .07f, -.008f),
                new Vector3(.029f, .205f, -.008f), new Vector3(-.017f, .269f, -.008f),
                new Vector3(-.033f, .07f, .008f), new Vector3(.031f, .07f, .008f),
                new Vector3(.029f, .205f, .008f), new Vector3(-.017f, .269f, .008f)
            };
            bladeMesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4,
                1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7 };
            bladeMesh.RecalculateNormals();
            steel.gameObject.AddComponent<MeshFilter>().sharedMesh = bladeMesh;
            steel.gameObject.AddComponent<MeshRenderer>().sharedMaterial = materials.Wall;
        }

        private static void BuildTemporaryGlove(Transform grip, PrototypeMaterials materials)
        {
            Box("Temporary glove palm", grip, new Vector3(.031f, -.005f, -.026f),
                new Vector3(.063f, .095f, .064f), materials.Metal, false);
            for (int i = 0; i < 4; i++)
                Box("Glove finger", grip, new Vector3(.003f, .033f - i * .022f, .027f),
                    new Vector3(.058f, .018f, .033f), materials.Metal, false);
            var sleeve = Box("Glove cuff and sleeve", grip, new Vector3(.075f, -.109f, -.088f),
                new Vector3(.084f, .176f, .078f), materials.Concrete, false);
            sleeve.transform.localRotation = Quaternion.Euler(29f, 0f, 19f);
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            SetRendering(false);
        }

        private void BeginCameraRendering(ScriptableRenderContext context, Camera camera) => SetRendering(camera == localCamera);
        private void EndCameraRendering(ScriptableRenderContext context, Camera camera) => SetRendering(false);

        private void SetRendering(bool visible)
        {
            if (renderers == null) return;
            foreach (Renderer renderer in renderers)
                if (renderer != null) renderer.forceRenderingOff = !visible;
        }

        private void OnDestroy()
        {
            if (bladeMesh != null) Destroy(bladeMesh);
            if (rightArmMesh != null) Destroy(rightArmMesh);
        }
    }
}
