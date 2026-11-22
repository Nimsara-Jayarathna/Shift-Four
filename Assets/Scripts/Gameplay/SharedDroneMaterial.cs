using System;
using UnityEngine;

namespace ShiftFour
{
    // Immutable shader defaults shared by live rigs. Per-drone feedback uses property blocks.
    internal sealed class SharedDroneMaterial
    {
        private readonly Func<Material> create;
        private Material material;
        private int users;

        internal SharedDroneMaterial(Func<Material> create) => this.create = create;

        internal Material Acquire()
        {
            if (material == null)
            {
                material = create();
                users = 0;
            }
            users++;
            return material;
        }

        internal void Release(ref Material lease)
        {
            if (lease == null) return;
            lease = null;
            if (--users != 0) return;
            UnityEngine.Object.Destroy(material);
            material = null;
        }
    }
}
