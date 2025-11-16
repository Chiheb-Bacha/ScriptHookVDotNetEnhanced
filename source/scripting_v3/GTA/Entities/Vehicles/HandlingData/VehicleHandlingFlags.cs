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
    public enum VehicleHandlingFlags : uint
    {
        None = 0x00000000,
        SmoothedCompression = 0x00000001,
        ReducedModMass = 0x00000002,
        HasKers = 0x00000004,
        HasRallyTyres = 0x00000008,
        NoHandbrake = 0x00000010,
        SteerRearWheels = 0x00000020,
        HandbrakeRearWheelSteer = 0x00000040,
        SteerAllWheels = 0x00000080,
        FreewheelNoGas = 0x00000100,
        NoReverse = 0x00000200,
        ReducedRightingForce = 0x00000400,
        SteerNoWheels = 0x00000800,
        CVT = 0x00001000,
        AltExtWheelBoundsBehavior = 0x00002000,
        DontRaiseBoundsAtSpeed = 0x00004000,
        ExtWheelBoundsCollide = 0x00008000,
        LessSnowSink = 0x00010000,
        TyresCanClip = 0x00020000,
        ReducedDriveOverDamage = 0x00040000,
        AltExtWheelBoundsShrink = 0x00080000,
        OffroadAbilities = 0x00100000,
        OffroadAbilitiesX2 = 0x00200000,
        TyresRaiseSideImpactThreshold = 0x00400000,
        OffroadIncreasedGravityNoFoliageDrag = 0x00800000,
        EnableLean = 0x01000000,
        ForceNoTcOrSc = 0x02000000,
        HeavyArmour = 0x04000000,
        Armoured = 0x08000000,
        SelfRightingInWater = 0x10000000,
        ImprovedRightingForce = 0x20000000,
        LowSpeedWheelies = 0x40000000,
        LastAvailableFlag = 0x80000000
    }
}
