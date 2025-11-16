//
// Copyright (C) 2025 Chiheb-Bacha
// License: https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced#license
//
// Source: https://gtamods.com/wiki/Handling.meta
//

using System;

namespace GTA
{
    /// <summary>
    /// A set of flags related to <see cref="Vehicle"/> handling.
    /// </summary>
    [Flags]
    public enum CarAdvancedFlags : uint
    {
        None = 0x00000000,
        DiffFront = 0x00000001,
        DiffRear = 0x00000002,
        DiffCentre = 0x00000004,
        DiffLimitedFront = 0x00000008,
        DiffLimitedRear = 0x00000010,
        DiffLimitedCentre = 0x00000020,
        DiffLockingFront = 0x00000040,
        DiffLockingRear = 0x00000080,
        DiffLockingCentre = 0x00000100,
        GearboxFullAuto = 0x00000200,
        GearboxManual = 0x00000400,
        GearboxDirectShift = 0x00000800,
        GearboxElectric = 0x00001000,
        AssistTractionControl = 0x00002000,
        AssistStabilityControl = 0x00004000,
        AllowReducedSuspensionForce = 0x00008000,
        HardRevLimit = 0x00010000,
        HoldGearWithWheelspin = 0x00020000,
        IncreaseSuspensionForceWithSpeed = 0x00040000,
        BlockIncreasedRotVelocityWithDriveForce = 0x00080000,
        ReducedSelfRightingSpeed = 0x00100000,
        CloseRatioGearbox = 0x00200000,
        ForceSmoothRpm = 0x00400000,
        AllowTurnOnSpot = 0x00800000,
        CanWheelie = 0x01000000,
        EnableWheelBlockerSideImpacts = 0x02000000,
        FixOldBugs = 0x04000000,
        UseDownforceBias = 0x08000000,
        ReduceBodyRollWithSuspensionMods = 0x10000000,
        AllowsExtendedMods = 0x20000000
    }
}
