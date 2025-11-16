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
    public enum VehicleDamageFlags : uint
    {
        None = 0x00000000,
        DriverSideFrontDoor = 0x00000001,
        DriverSideRearDoor = 0x00000002,
        DriverPassengerSideFrontDoor = 0x00000004,
        DriverPassengerSideRearDoor = 0x00000008,
        Bonnet = 0x00000010,
        Boot = 0x00000020
    }
}
