using System.Collections.Generic;
using UnityEngine;
using Site = ShiftFour.PrototypePlayerFeedback.Site;

namespace ShiftFour
{
    // Snapshot the static, axis-aligned layout when the generator binds its missions.
    internal sealed class FacilityMap
    {
        private readonly Transform[] markers = new Transform[(int)Site.Extraction + 1];
        internal readonly List<Bounds> Roads = new();
        internal readonly Bounds[] Foundations = new Bounds[4];
        internal Bounds Ground { get; private set; }
        internal Transform Spawn { get; private set; }
        internal Transform Marker(Site site) => markers[(int)site];

        internal void Bind(Transform layout, Transform spawn)
        {
            Roads.Clear();
            Spawn = spawn;
            Ground = Footprint(layout.Find("Ground"));
            var roads = layout.Find("Roads");
            if (roads != null)
                foreach (Transform road in roads)
                    if (road.TryGetComponent<BoxCollider>(out var slab) && slab.enabled)
                        Roads.Add(slab.bounds);

            var buildings = layout.Find("Buildings");
            for (int i = 0; i < Foundations.Length; i++)
            {
                var site = (Site)(i + 1);
                var building = buildings != null ? buildings.Find(site.ToString()) : null;
                markers[(int)site] = building;
                Foundations[i] = Footprint(building != null ? building.Find("Foundation") : null);
            }
            markers[(int)Site.Supply] = layout.Find(OutdoorSupplyCache.HierarchyName);
            markers[(int)Site.Extraction] = layout.Find("Final Extraction Gate");
        }

        private static Bounds Footprint(Transform item) =>
            item != null && item.TryGetComponent<BoxCollider>(out var collider) ? collider.bounds : default;

        internal Vector2 Project(Vector3 point, Rect frame)
        {
            float scale = Mathf.Min(frame.width / Ground.size.x, frame.height / Ground.size.z);
            return frame.center + new Vector2(point.x - Ground.center.x, Ground.center.z - point.z) * scale;
        }

        internal Rect ProjectBounds(Bounds bounds, Rect frame)
        {
            Vector2 sw = Project(bounds.min, frame), ne = Project(bounds.max, frame);
            return Rect.MinMaxRect(sw.x, ne.y, ne.x, sw.y);
        }
    }
}
