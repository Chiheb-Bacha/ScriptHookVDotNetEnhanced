//
// Copyright (C) 2025 Chiheb-Bacha
// License: https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced#license
//

using System;
using SHVDN;

namespace GTA
{
    public class CAIHandlingInfo
    {
        internal CAIHandlingInfo(IntPtr MemoryAddress)
        {
            this.MemoryAddress = MemoryAddress;
        }

        /// <summary>
        /// Gets the memory address where the <see cref="CAIHandlingInfo"/> is stored in memory.
        /// </summary>
        public IntPtr MemoryAddress;

        /// <summary>
        /// Gets the value indicating whether this <see cref="CAIHandlingInfo"/> is valid, i.e. has a non-null <see cref="MemoryAddress"/>.
        /// </summary>
        public bool IsValid => MemoryAddress != IntPtr.Zero;

        /// <summary>
        /// Gets the <see cref="CAIHandlingInfo"/> for a given <see cref="AIHandlingHash"/>.
        /// </summary>
        /// <param name="hash">The <see cref="AIHandlingHash"/> which this <see cref="CAIHandlingInfo"/> belongs to</param>
        /// <returns>
        /// A <see cref="CAIHandlingInfo"/> belonging to the given <see cref="AIHandlingHash"/>
        /// </returns>
        public static CAIHandlingInfo GetByHash(AIHandlingHash hash)
        {
            return new CAIHandlingInfo(NativeMemory.GetCAIInfoByHash((uint)hash));
        }

        /// <summary>
        /// Gets the <see cref="AIHandlingHash"/> of the name of this <see cref="CAIHandlingInfo"/>-
        /// </summary>
        public AIHandlingHash NameHash
        {
            get
            {
                if (!IsValid)
                {
                    return 0;
                }
                return (AIHandlingHash)MemDataMarshal.ReadUInt32(MemoryAddress + 0x08); // TODO: Find this offset dynamically
            }
        }

        /// <summary>
        /// Gets or sets the minimum brake distance the AI will use if the <see cref="HandlingData"/> has this <see cref="CAIHandlingInfo"/>.
        /// </summary>
        public float MinBrakeDistance
        {
            get
            {
                if (!IsValid)
                {
                    return 0;
                }
                return MemDataMarshal.ReadFloat(MemoryAddress + 0x0C); // TODO: Find this offset dynamically
            }
            set
            {
                if (!IsValid)
                {
                    return;
                }
                MemDataMarshal.WriteFloat(MemoryAddress + 0x0C, value); // TODO: Find this offset dynamically
            }
        }

        /// <summary>
        /// Gets or sets the maximum brake distance the AI will use if the <see cref="HandlingData"/> has this <see cref="CAIHandlingInfo"/>.
        /// </summary>
        public float MaxBrakeDistance
        {
            get
            {
                if (!IsValid)
                {
                    return 0;
                }
                return MemDataMarshal.ReadFloat(MemoryAddress + 0x10); // TODO: Find this offset dynamically
            }
            set
            {
                if (!IsValid)
                {
                    return;
                }
                MemDataMarshal.WriteFloat(MemoryAddress + 0x10, value); // TODO: Find this offset dynamically
            }
        }

        /// <summary>
        /// Gets or sets the maximum speed at brake distance the AI will use if the <see cref="HandlingData"/> has this <see cref="CAIHandlingInfo"/>.
        /// </summary>
        public float MaxSpeedAtBrakeDistance
        {
            get
            {
                if (!IsValid)
                {
                    return 0;
                }
                return MemDataMarshal.ReadFloat(MemoryAddress + 0x14); // TODO: Find this offset dynamically
            }
            set
            {
                if (!IsValid)
                {
                    return;
                }
                MemDataMarshal.WriteFloat(MemoryAddress + 0x14, value); // TODO: Find this offset dynamically
            }
        }

        /// <summary>
        /// Gets or sets the absolute minimum speed the AI will use if the <see cref="HandlingData"/> has this <see cref="CAIHandlingInfo"/>.
        /// </summary>
        public float AbsoluteMinSpeed
        {
            get
            {
                if (!IsValid)
                {
                    return 0;
                }
                return MemDataMarshal.ReadFloat(MemoryAddress + 0x18); // TODO: Find this offset dynamically
            }
            set
            {
                if (!IsValid)
                {
                    return;
                }
                MemDataMarshal.WriteFloat(MemoryAddress + 0x18, value); // TODO: Find this offset dynamically
            }
        }

        internal ushort CAICurvePointsCount
        {
            get
            {
                if (!IsValid)
                {
                    return 0;
                }
                return MemDataMarshal.ReadUInt16(MemoryAddress + NativeMemory.s_CAICurvePointCountInCAIHandlingInfoOffset); // 0x28
            }
        }

        internal IntPtr CAICurvePointsBase
        {
            get
            {
                if (!IsValid)
                {
                    return IntPtr.Zero;
                }
                return MemDataMarshal.ReadAddress(MemoryAddress + NativeMemory.s_CAICurvePointBaseInCAIHandlingInfoOffset); // 0x20
            }
        }

        /// <summary>
        /// Gets all <see cref="CAICurvePoint"/>s of this <see cref="CAIHandlingInfo"/>.
        /// </summary>
        /// <returns>
        /// An array of <see cref="CAICurvePoint"/> belonding to this <see cref="CAIHandlingInfo"/>
        /// </returns>
        public CAICurvePoint[] GetAllCAICurvePoints()
        {
            ushort cAiCurvePointsCount = CAICurvePointsCount;
            var cAICurvePoints = new CAICurvePoint[cAiCurvePointsCount];
            for (int i = 0; i < cAiCurvePointsCount; i++)
            {
                cAICurvePoints[i] = new CAICurvePoint(MemDataMarshal.ReadAddress(CAICurvePointsBase + i * 8));
            }
            return cAICurvePoints;
        }
    }

}
