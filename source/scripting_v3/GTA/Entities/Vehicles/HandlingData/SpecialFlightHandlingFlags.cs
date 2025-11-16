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
    public enum SpecialFlightHandlingFlags : uint
    {
        None = 0x00000000,
        WorksUpsideDown = 0x00000001,
        DontRetractWheels = 0x00000002,
        ReduceDragWithSpeed = 0x00000004,
        MaintainHoverHeight = 0x00000008,
        SteerTowardsVelocity = 0x00000010,
        ForceMinThrottleWhenTurning = 0x00000020,
        LimitForceDelta = 0x00000040,
        ForceSpecialFlightWhenDriven = 0x00000080
    }
}
