//
// Copyright (C) 2025 Chiheb-Bacha
// License: https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced#license
//

using System;
using SHVDN;

namespace GTA
{
    public class CAICurvePoint
    {
        /// <summary>
        /// Gets the memory address where the <see cref="CAICurvePoint"/> is stored in memory.
        /// </summary>
        public IntPtr MemoryAddress;

        /// <summary>
        /// Gets the value indicating whether this <see cref="CAICurvePoint"/> is valid, i.e. has a non-null <see cref="MemoryAddress"/>.
        /// </summary>
        public bool IsValid => MemoryAddress != IntPtr.Zero;

        /// <summary>
        /// Gets or sets the angle of this <see cref="CAICurvePoint"/>.
        /// </summary>
        public float Angle
        {
            get
            {
                if (!IsValid)
                {
                    return 0.0f;
                }
                return MemDataMarshal.ReadFloat(MemoryAddress + 0x08); // TODO: Get this offset dynamically
            }

            set
            {
                if (!IsValid)
                {
                    return;
                }
                MemDataMarshal.WriteFloat(MemoryAddress + 0x08, value); // TODO: Get this offset dynamically
            }
        }

        /// <summary>
        /// Gets or sets the speed of this <see cref="CAICurvePoint"/>.
        /// </summary>
        public float Speed
        {
            get
            {
                if (!IsValid)
                {
                    return 0.0f;
                }
                return MemDataMarshal.ReadFloat(MemoryAddress + 0x0C); // TODO: Get this offset dynamically
            }

            set
            {
                if (!IsValid)
                {
                    return;
                }
                MemDataMarshal.WriteFloat(MemoryAddress + 0x0C, value); // TODO: Get this offset dynamically
            }
        }

        internal CAICurvePoint(IntPtr MemoryAddress)
        {
            this.MemoryAddress = MemoryAddress;
        }

    }
}
