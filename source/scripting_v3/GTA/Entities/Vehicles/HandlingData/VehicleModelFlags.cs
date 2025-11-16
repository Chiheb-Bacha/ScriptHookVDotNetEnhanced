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
    /// A set of flags related to <see cref="Vehicle"/> models.
    /// </summary>
    [Flags]
    public enum VehicleModelFlags : uint
    {
        None = 0x00000000,
        IsVan = 0x00000001,
        IsBus = 0x00000002,
        IsLow = 0x00000004,
        IsBig = 0x00000008,
        AbsStd = 0x00000010,
        AbsOption = 0x00000020,
        AbsAltStd = 0x00000040,
        AbsAltOption = 0x00000080,
        NoDoors = 0x00000100,
        TandemSeating = 0x00000200,
        SitInBoat = 0x00000400,
        HasTracks = 0x00000800,
        NoExhaust = 0x00001000,
        DoubleExhaust = 0x00002000,
        NoFirstPersonLookBehind = 0x00004000,
        CanEnterIfNoDoor = 0x00008000,
        AxleFTorsion = 0x00010000,
        AxleFSolid = 0x00020000,
        AxleFMcpherson = 0x00040000,
        AttachPedToBodyShell = 0x00080000,
        AxleRTorsion = 0x00100000,
        AxleRSolid = 0x00200000,
        AxleRMcpherson = 0x00400000,
        DontForceGroundClearance = 0x00800000,
        DontRenderSteer = 0x01000000,
        NoWheelBurst = 0x02000000,
        Indestructible = 0x04000000,
        DoubleFrontWheels = 0x08000000,
        IsRc = 0x10000000,
        DoubleRearWheels = 0x20000000,
        NoWheelBreak = 0x40000000,
        ExtraCamber = 0x80000000
    }
}
