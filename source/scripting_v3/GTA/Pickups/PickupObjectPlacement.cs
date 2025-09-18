//
// Copyright (C) 2025 Chiheb-Bacha
// License: https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced#license
//

using System;
using GTA.Math;
using SHVDN;

namespace GTA
{
    /// <summary>
    /// Represents a pickup placement, not pickup object.
    /// </summary>
    public sealed class PickupObjectPlacement
    {
        public PickupObjectPlacement(ulong address) {
            this.MemoryAddress = new IntPtr((long)address);
        }

        public Vector3 Position
        {
            get
            {
                float[] pos = NativeMemory.GetPickupObjectPlacementPosition(MemoryAddress);
                return new Vector3(pos[0], pos[1], pos[2]);
            }
        }

        public IntPtr MemoryAddress;
    }
}
