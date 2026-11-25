using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShiftFour
{
    // Owned by the outdoor menu for exactly one scene lifetime.
    internal sealed class OutdoorSettings
    {
        internal enum Preset { Low, Balanced, High }
        internal struct Values
        {
            internal Preset Quality;
            internal int Master, Music, Effects;
            internal float Sensitivity;
            internal Vector2Int Size;
            internal bool Fullscreen;
        }

        private const string Prefix = "ShiftFour.OpenWorld.Settings.v1.";
        private readonly PlayerController player;
        private readonly GameplayAudio audio;
        private readonly WorldGenerator world;
        private readonly float originalVolume;
        private readonly RenderPipelineAsset originalOverride;
        private readonly UniversalRenderPipelineAsset pipeline;
        private UniversalRenderPipelineAsset runtimePipeline;
        internal readonly List<Vector2Int> Resolutions = new List<Vector2Int>();
        internal Values Applied { get; private set; }
        internal Values Draft;
        internal bool DisplayAvailable => !Application.isEditor && Resolutions.Count > 0;
        internal bool Dirty => !Draft.Equals(Applied);

        internal OutdoorSettings(PlayerController actor, WorldGenerator generator)
        {
            player = actor;
            world = generator;
            audio = actor.GetComponent<GameplayAudio>();
            originalVolume = AudioListener.volume;
            originalOverride = QualitySettings.renderPipeline;
            pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            RefreshResolutions();
            var value = Defaults();
            value.Quality = (Preset)PlayerPrefs.GetInt(Prefix + "quality", (int)value.Quality);
            value.Master = PlayerPrefs.GetInt(Prefix + "master", value.Master);
            value.Music = PlayerPrefs.GetInt(Prefix + "music", value.Music);
            value.Effects = PlayerPrefs.GetInt(Prefix + "sfx", value.Effects);
            value.Sensitivity = PlayerPrefs.GetFloat(Prefix + "sensitivity", value.Sensitivity);
            bool savedDisplay = PlayerPrefs.HasKey(Prefix + "width");
            value.Size = savedDisplay ? new Vector2Int(PlayerPrefs.GetInt(Prefix + "width"), PlayerPrefs.GetInt(Prefix + "height"))
                : new Vector2Int(Screen.width, Screen.height);
            value.Fullscreen = PlayerPrefs.GetInt(Prefix + "fullscreen", Screen.fullScreen ? 1 : 0) == 1;
            Applied = Draft = Validate(value);
            ApplySoundAndControls(Applied);
            SetQuality(Applied.Quality);
            if (savedDisplay && DisplayAvailable) world.StartCoroutine(RestoreDisplay());
        }

        private IEnumerator RestoreDisplay()
        {
            // macOS can finish its native fullscreen transition after the first rendered frame.
            yield return null;
            SetDisplay(Applied);
            yield return new WaitForSecondsRealtime(.5f);
            SetDisplay(Applied);
        }

        internal void PreviewMusic(bool enabled) => audio.PreviewSettingsMusic(enabled);

        internal void Begin()
        {
            RefreshResolutions();
            Draft = Validate(Applied);
        }

        private void RefreshResolutions()
        {
            Resolutions.Clear();
            if (Application.isEditor) return;
            foreach (Resolution mode in Screen.resolutions)
            {
                var size = new Vector2Int(mode.width, mode.height);
                if (size.x > 0 && size.y > 0 && !Resolutions.Contains(size)) Resolutions.Add(size);
            }
            Resolutions.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        }

        internal Values Defaults()
        {
            var desktop = Screen.currentResolution;
            return new Values { Quality = Preset.Balanced, Master = 100, Music = 100, Effects = 100,
                Sensitivity = 2f, Fullscreen = DisplayAvailable || Screen.fullScreen, Size = Supported(new Vector2Int(desktop.width, desktop.height)) };
        }

        private Vector2Int Supported(Vector2Int requested)
        {
            if (Resolutions.Contains(requested)) return requested;
            var current = new Vector2Int(Screen.width, Screen.height);
            if (Resolutions.Count == 0 || Resolutions.Contains(current)) return current;
            var desktop = Screen.currentResolution;
            var native = new Vector2Int(desktop.width, desktop.height);
            return Resolutions.Contains(native) ? native : Resolutions[Resolutions.Count - 1];
        }

        private Values Validate(Values v)
        {
            if ((uint)v.Quality > (uint)Preset.High) v.Quality = Preset.Balanced;
            v.Master = Mathf.Clamp(v.Master, 0, 100);
            v.Music = Mathf.Clamp(v.Music, 0, 100);
            v.Effects = Mathf.Clamp(v.Effects, 0, 100);
            v.Sensitivity = float.IsFinite(v.Sensitivity) ? Mathf.Clamp(v.Sensitivity, .2f, 5f) : 2f;
            // A resized desktop window can have a valid size absent from the monitor's fullscreen modes.
            if (v.Fullscreen || Screen.fullScreen || v.Size != new Vector2Int(Screen.width, Screen.height))
                v.Size = Supported(v.Size);
            return v;
        }

        internal void Apply()
        {
            RefreshResolutions();
            var next = Validate(Draft);
            ApplySoundAndControls(next);
            if (next.Quality != Applied.Quality) SetQuality(next.Quality);
            bool displayChanged = DisplayAvailable && (next.Size != Applied.Size || next.Fullscreen != Applied.Fullscreen);
            if (displayChanged)
                SetDisplay(next);
            PlayerPrefs.SetInt(Prefix + "quality", (int)next.Quality);
            PlayerPrefs.SetInt(Prefix + "master", next.Master);
            PlayerPrefs.SetInt(Prefix + "music", next.Music);
            PlayerPrefs.SetInt(Prefix + "sfx", next.Effects);
            PlayerPrefs.SetFloat(Prefix + "sensitivity", next.Sensitivity);
            if (displayChanged)
            {
                PlayerPrefs.SetInt(Prefix + "width", next.Size.x);
                PlayerPrefs.SetInt(Prefix + "height", next.Size.y);
                PlayerPrefs.SetInt(Prefix + "fullscreen", next.Fullscreen ? 1 : 0);
            }
            PlayerPrefs.Save();
            Applied = Draft = next;
        }

        private void SetDisplay(Values v)
        {
            if (!DisplayAvailable) return;
            var mode = v.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (Screen.width != v.Size.x || Screen.height != v.Size.y || Screen.fullScreenMode != mode)
                Screen.SetResolution(v.Size.x, v.Size.y, mode);
        }

        private void ApplySoundAndControls(Values v)
        {
            player.MouseSensitivity = v.Sensitivity;
            AudioListener.volume = v.Master / 100f;
            audio.SetCategoryVolumes(v.Music / 100f, v.Effects / 100f);
        }

        private void SetQuality(Preset preset)
        {
            if (pipeline != null)
            {
                if (preset == Preset.Balanced)
                    QualitySettings.renderPipeline = originalOverride;
                else
                {
                    if (runtimePipeline == null)
                    {
                        runtimePipeline = Object.Instantiate(pipeline);
                        runtimePipeline.name = "Outdoor quality (runtime)";
                        runtimePipeline.hideFlags = HideFlags.DontSave;
                    }
                    bool low = preset == Preset.Low;
                    runtimePipeline.shadowDistance = low ? 28f : 75f;
                    runtimePipeline.mainLightShadowmapResolution = low ? 1024 : 4096;
                    runtimePipeline.renderScale = low ? .8f : 1f;
                    runtimePipeline.msaaSampleCount = low ? 1 : 2;
                    QualitySettings.renderPipeline = runtimePipeline;
                }
            }
            world.GetComponentInChildren<OutdoorScenery>()?.SetReducedGrass(preset == Preset.Low);
        }

        internal void Dispose()
        {
            AudioListener.volume = originalVolume;
            if (runtimePipeline != null)
            {
                if (QualitySettings.renderPipeline == runtimePipeline) QualitySettings.renderPipeline = originalOverride;
                Object.Destroy(runtimePipeline);
            }
        }
    }
}
