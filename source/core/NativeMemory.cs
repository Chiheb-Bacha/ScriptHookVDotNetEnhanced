//
// Copyright (C) 2025 Chiheb-Bacha
// License: https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced#license
//

using System;
using System.Drawing;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using static System.Runtime.InteropServices.Marshal;
using static SHVDN.NativeMemory;
using SHVDN;
using System.CodeDom;
using System.Net;

namespace SHVDN
{
    /// <summary>
    /// Class responsible for managing all access to game memory.
    /// </summary>
    public static unsafe class NativeMemory
    {
        #region ScriptHookV Imports
        /// <summary>
        /// Creates a texture. Texture deletion is performed automatically when game reloads scripts.
        /// Can be called only in the same thread as natives.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns>Internal texture ID.</returns>
        [SuppressUnmanagedCodeSecurity]
        [DllImport("ScriptHookV.dll", ExactSpelling = true, EntryPoint = "?createTexture@@YAHPEBD@Z")]
        public static extern int CreateTexture([MarshalAs(UnmanagedType.LPStr)] string filename);

        /// <summary>
        /// Draws a texture on screen. Can be called only in the same thread as natives.
        /// </summary>
        /// <param name="id">Texture ID returned by <see cref="CreateTexture(string)"/>.</param>
        /// <param name="instance">The instance index. Each texture can have up to 64 different instances on screen at a time.</param>
        /// <param name="level">Texture instance with low levels draw first.</param>
        /// <param name="time">How long in milliseconds the texture instance should stay on screen.</param>
        /// <param name="sizeX">Width in screen space [0,1].</param>
        /// <param name="sizeY">Height in screen space [0,1].</param>
        /// <param name="centerX">Center position in texture space [0,1].</param>
        /// <param name="centerY">Center position in texture space [0,1].</param>
        /// <param name="posX">Position in screen space [0,1].</param>
        /// <param name="posY">Position in screen space [0,1].</param>
        /// <param name="rotation">Normalized rotation [0,1].</param>
        /// <param name="scaleFactor">Screen aspect ratio, used for size correction.</param>
        /// <param name="colorR">Red tint.</param>
        /// <param name="colorG">Green tint.</param>
        /// <param name="colorB">Blue tint.</param>
        /// <param name="colorA">Alpha value.</param>
        [SuppressUnmanagedCodeSecurity]
        [DllImport("ScriptHookV.dll", ExactSpelling = true, EntryPoint = "?drawTexture@@YAXHHHHMMMMMMMMMMMM@Z")]
        public static extern void DrawTexture(int id, int instance, int level, int time, float sizeX, float sizeY, float centerX, float centerY, float posX, float posY, float rotation, float scaleFactor, float colorR, float colorG, float colorB, float colorA);

        /// <summary>
        /// Gets the game version enumeration value as specified by ScriptHookV.
        /// </summary>
        [DllImport("ScriptHookV.dll", ExactSpelling = true, EntryPoint = "?getGameVersion@@YA?AW4eGameVersion@@XZ")]
        public static extern int GetGameVersion();

        /// <summary>
        /// Returns pointer to a global variable. IDs may differ between game versions.
        /// </summary>
        /// <param name="index">The variable ID to query.</param>
        /// <returns>Pointer to the variable, or <see cref="IntPtr.Zero"/> if it does not exist.</returns>
        [SuppressUnmanagedCodeSecurity]
        [DllImport("ScriptHookV.dll", ExactSpelling = true, EntryPoint = "?getGlobalPtr@@YAPEA_KH@Z")]
        public static extern IntPtr GetGlobalPtr(int index);
        #endregion

        /// <summary>
        /// Disposes unmanaged resources.
        /// </summary>
        internal static void DisposeUnmanagedResources()
        {
            Marshal.FreeCoTaskMem(String);
            Marshal.FreeCoTaskMem(NullString);
            Marshal.FreeCoTaskMem(CellEmailBcon);

            String = IntPtr.Zero;
            NullString = IntPtr.Zero;
            CellEmailBcon = IntPtr.Zero;
        }

        /// <summary>
        /// Initializes all known functions and offsets based on pattern searching.
        /// </summary>
        static NativeMemory()
        {
            String = StringMarshal.StringToCoTaskMemUtf8("STRING"); // "~a~"
            NullString = StringMarshal.StringToCoTaskMemUtf8(string.Empty); // ""
            CellEmailBcon = StringMarshal.StringToCoTaskMemUtf8("CELL_EMAIL_BCON"); // "~a~~a~~a~~a~~a~~a~~a~~a~~a~~a~"

            s_isEnhanced = Process.GetCurrentProcess().MainModule.ModuleName.Equals("GTA5_Enhanced.exe");

            // There are hundreds of checks against GameFileVersion in the APIs, which only take Legacy into consideration.
            // That means, conditions could apply to Enhanced when they shouldn't, given that its GameFileVersion is lower than that of Legacy.
            // For that reason, we bump the Major if Enhanced is running, instead of modifying hundreds of checks in the API.
            var version = new Version(FileVersionInfo.GetVersionInfo(Process.GetCurrentProcess().MainModule.FileName).FileVersion);
            GameFileVersion =  s_isEnhanced ? new Version(version.Major + 1, version.Minor, version.Build, version.Revision) : version;

            byte* address;
            IntPtr startAddressToSearch;

            // Get relative address and add it to the instruction address.

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 8b 14 08 48 89 f1 e8");
                if (address != null)
                {
                    // This function encompasses both AddKnownRef and RemoveKnownRef.
                    // In Legacy, both functions include a Lock then Unlock to what seems to be a synchronization lock.
                    // In Enhanced, that is done outside of the two functions, but inside this new function.
                    // This has the same effect as to what SHVDN is doing in legacy.
                    // This new function also checks that the passed _lhs is present inside the pool of knownRefs before trying to remove it.
                    //
                    // SHVDN uses these functions when setting ProjectileRocket.Target, but doesn't seem to have any effect in both Editions.
                    // If you lock on a Target with a homing missile, shoot it, then change the Target, the missile will stop following the old target.
                    // It doesn't however redirect to the new target.
                    // It could have different triggers that I'm not aware of, so I might revisit this in the future.
                    s_fwRefAwareBaseImpl__RemoveThenAddKnownRef = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)(new IntPtr(
                        *(int*)(address + 8) + address + 12));
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x74\x27\x48\x8D\x7E\x18\x48\x8B\x0F\x48\x3B\xCB\x74\x1B", "xxxxxxxxxxxxxx");
                if (address != null)
                {
                    // Fetch the address of `AddKnownRef` first, as the offset is at like plus 0xA5 in any builds,
                    // while that of `RemoveKnownRef` is at like plus 0x18EB4.
                    s_fwRefAwareBaseImpl__AddKnownRef = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)(new IntPtr(
                        *(int*)(address + 0x25) + address + 0x29));
                    s_fwRefAwareBaseImpl__RemoveKnownRef = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)(new IntPtr(
                        *(int*)(address + 0x17) + address + 0x1B));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("89 f1 0f 28 ce e8 ? ? ? ? 0f 28 b4");
                if (address != null)
                {
                    address = (byte*)(*(int*)(address + 6) + address + 10);
                    s_PtfxVfuncSecondArgumentFuncAddr = (ulong)(*(int*)(address + 31) + address + 35);
                    s_PtfxHashTableCount = (short*)(*(int*)(address + 50) + address + 54);
                    s_PtfxHashTableBuckets = (ulong*)(*(int*)(address + 72) + address + 76);
                }
            }
            else
            { 
                address = MemScanner.FindPatternBmh("\x74\x21\x48\x8B\x48\x20\x48\x85\xC9\x74\x18\x48\x8B\xD6\xE8", "xxxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_getPtfxAddressFunc = (delegate* unmanaged[Stdcall]<int, ulong>)(
                        new IntPtr(*(int*)(address - 10) + address - 6));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("41 0f 28 9e ? ? ? ? f3 41 0f 10 86");
            }
            else
            {
                address = MemScanner.FindPatternBmh("41 0f 28 96 ? ? ? ? f3 41 0f 10 8e");
            }
            if (address != null)
            {
                PtfxColorOffset = *(int*)(address + 4); // 0x140
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 8b 45 ? 4d 85 ff");
            }
            else
            {
                address = MemScanner.FindPatternBmh("48 8b 48 ? 48 8d 54 24 ? 0f 28 d6");
            }
            if (address != null)
            {
                PtfxBaseOffset = (int)*(byte*)(address + 3); // 0x20
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("0f 2e 05 ? ? ? ? 76 ? f3 0f 11 84"); // 0x184
            }
            else
            {
                address = MemScanner.FindPatternBmh("0f 2f 05 ? ? ? ? 76 ? 83 c9"); // 0x180
            }
            if (address != null)
            {
                PtfxRangeOffset = (int)*(byte*)(address - 4);
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("41 0f 59 97 ? ? ? ? f3 0f 59 d0");
            }
            else
            {
                address = MemScanner.FindPatternBmh("f3 0f 10 83 ? ? ? ? 0f c6 c0 ? 0f 58 d1");
            }
            if (address != null)
            {
                PtfxScaleOffset = (int)*(byte*)(address + 4); // 0x150
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("41 8b 4c 1c ? e8");
                if (address != null)
                {
                    s_getScriptEntity = (delegate* unmanaged[Stdcall]<int, ulong>)(
                        new IntPtr(*(int*)(address + 6) + address + 10));
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x85\xED\x74\x0F\x8B\xCD\xE8\x00\x00\x00\x00\x48\x8B\xF8\x48\x85\xC0\x74\x2E", "xxxxxxx????xxxxxxxx");
                if (address != null)
                {
                    s_getScriptEntity = (delegate* unmanaged[Stdcall]<int, ulong>)(
                        new IntPtr(*(int*)(address + 7) + address + 11));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("89 f1 b2 ? e8 ? ? ? ? 31 c9 48");
                if (address != null)
                {
                    s_getPlayerPedAddressFunc = (delegate* unmanaged[Stdcall]<int, ulong>)(
                    new IntPtr(*(int*)(address + 5) + address + 9));
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x8B\xC2\xB2\x01\x8B\xC8\xE8\x00\x00\x00\x00\x48\x85\xC0\x74\x53\x8A\x88\x00\x00\x00\x00\xF6\xC1\x01\x75\x05\xF6\xC1\x02\x75\x43\x48", "xxxxxxx????xxxxxxx????xxxxxxxxxxx");
                if (address != null)
                {
                    s_getPlayerPedAddressFunc = (delegate* unmanaged[Stdcall]<int, ulong>)(
                    new IntPtr(*(int*)(address + 7) + address + 11));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("80 3d ? ? ? ? ? 0f 84 ? ? ? ? 48 8b 0d ? ? ? ? 8b 81");
                if (address != null)
                {
                    s_isGameMultiplayerAddr = (bool*)(*(int*)(address + 2) + address + 7);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x0F\x84\xA1\x00\x00\x00\x33\xC9\x48\x89\x35", "xxxxxxxxxxx");
                if (address != null)
                {
                    s_isGameMultiplayerAddr = (bool*)(*(int*)(address + 0x27) + address + 0x2B);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 8b ? ? ? ? ? 48 01 d9 e8 ? ? ? ? 48 8b");
                if (address != null)
                {
                    s_createGuid = (delegate* unmanaged[Stdcall]<ulong, int>)(
                        new IntPtr(*(int*)(address + 11) + address + 15));
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\xF7\xF9\x49\x8B\x48\x08\x48\x63\xD0\xC1\xE0\x08\x0F\xB6\x1C\x11\x03\xD8", "xxxxxxxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_createGuid = (delegate* unmanaged[Stdcall]<ulong, int>)(
                        new IntPtr(address - 0x68));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("0f 84 ? ? ? ? f3 0f 10 bc 24 30 01 00 00");
                if (address != null)
                {
                    s_entityPosVFuncSecondArgument = (ulong)(*(int*)(address - 12) + address - 8);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x40\x53\x48\x83\xEC\x30\x48\x8B\xDA\xE8\x00\x00\x00\x00\xF3\x0F\x10\x44\x24\x2C\x33\xC9\xF3\x0F\x11\x43\x0C\x48\x89\x0B\x89\x4B\x08\x48\x85\xC0\x74\x2C", "xxxxxxxxxx????xxxxxxxxxxxxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_entityPosFunc = (delegate* unmanaged[Stdcall]<ulong, float*, ulong>)(address);
                }
            }

            // Find handling data functions

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 83 ec ? e8 ? ? ? ? 48 83 c4 ? 8b be");
                if (address != null)
                {
                    s_getHandlingDataByIndex = (delegate* unmanaged[Stdcall]<int, ulong>)(new IntPtr(*(int*)(address + 5) + address + 9));
                    s_handlingIndexOffsetInModelInfo = *(int*)(address - 4);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x8B\xF7\x83\xF8\x01\x77\x08\x44\x8B\xF7\x8D\x77\xFF\xEB\x06\x41\xBE\x03\x00\x00\x00\x8B\x8B", "xxxxxxxxxxxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_getHandlingDataByIndex = (delegate* unmanaged[Stdcall]<int, ulong>)(new IntPtr(*(int*)(address + 28) + address + 32));
                    s_handlingIndexOffsetInModelInfo = *(int*)(address + 23);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("b8 ff ff ff ff 84 d2 74 ? 44 0f b7 05");
                s_gHandlingInfoBase = *(ulong*)(*(int*)(address + 28) + address + 32);
                s_getHandlingDataIndexByHash = (delegate* unmanaged[Stdcall]<IntPtr, bool, int>)(
                        new IntPtr(address));
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x75\x5A\xB2\x01\x48\x8B\xCB\xE8\x00\x00\x00\x00\x41\x8B\xF5\x66\x44\x3B\xAB", "xxxxxxxx????xxxxxxx");
                if (address != null)
                {
                    s_getHandlingDataByHash = (delegate* unmanaged[Stdcall]<IntPtr, ulong>)(
                        new IntPtr(*(int*)(address - 7) + address - 3));
                }
            }

            // Find entity pools and interior proxy pool

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 83 ec ? 83 3d ? ? ? ? ? 0f 84 ? ? ? ? 0f b6 05");
                if (address != null) {
                    bool isInitialized = (*(byte*)(*(int*)(address + 20) + address + 24) & 1) != 0;
                    ulong firstValue = *(ulong*)(*(int*)(address + 38) + address + 42);
                    ulong secondValue = *(ulong*)(*(int*)(address + 27) + address + 31);
                    var firstRol = *(byte*)(address + 34); // 0x1E
                    var secondRol = *(byte*)(address + 48); // 0x20
                    var andValue = *(byte*)(address + 51); // 0x1F
                    var addValue = *(byte*)(address + 54); // 2
                    s_pedPoolAddress = (ulong*)0UL;
                    if (isInitialized)
                    {
                        var rcx = secondValue;
                        rcx = Rol(rcx, firstRol);
                        var rax = firstValue;
                        rax = rax ^ rcx;
                        rax = Rol(rax, secondRol);
                        var cl = (byte)(rcx & 0xFF);
                        cl = (byte)(cl & andValue);
                        cl = (byte)(cl + addValue);
                        rax = Rol(rax, cl);
                        rax = ~rax;
                        s_pedPoolAddress = (ulong*)rax;
                    }
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x8B\x05\x00\x00\x00\x00\x41\x0F\xBF\xC8\x0F\xBF\x40\x10", "xxx????xxxxxxxx");
                if (address != null)
                {
                    s_pedPoolAddress = (ulong*)(*(int*)(address + 3) + address + 7);
                }
            }
            
            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("53 48 81 ec ? ? ? ? 0f 29 b4 ? ? ? ? ? 48 89 ce 0f b6 05");
                if (address != null)
                {
                    bool isInitialized = (*(byte*)(*(int*)(address + 22) + address + 26) & 1) != 0;
                    ulong firstValue = *(ulong*)(*(int*)(address + 40) + address + 44);
                    ulong secondValue = *(ulong*)(*(int*)(address + 29) + address + 33);
                    var firstRol = *(byte*)(address + 36); // 0x1E
                    var secondRol = *(byte*)(address + 59); // 0x20
                    var andValue = *(byte*)(address + 49); // 0x1F
                    var addValue = *(byte*)(address + 52); // 3
                    s_objectPoolAddress = (ulong*)0UL;
                    if (isInitialized)
                    {
                        var rcx = secondValue;
                        rcx = Rol(rcx, firstRol);
                        var rdx = firstValue;
                        rdx = rdx ^ rcx;
                        var cl = (byte)(rcx & 0xFF);
                        cl = (byte)(cl & andValue);
                        cl = (byte)(cl + addValue);
                        rdx = Rol(rdx, cl);
                        rdx = Rol(rdx, secondRol);
                        rdx = ~rdx;
                        s_objectPoolAddress = (ulong*)rdx;
                    }
                }
            } else
            {
                address = MemScanner.FindPatternBmh("\x48\x8B\x05\x00\x00\x00\x00\x8B\x78\x10\x85\xFF", "xxx????xxxxx");
                if (address != null)
                {
                    s_objectPoolAddress = (ulong*)(*(int*)(address + 3) + address + 7);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("83 f9 ? 74 ? 41 89 c8");
                if (address != null)
                {
                    bool isInitialized = (*(byte*)(*(int*)(address + 11) + address + 15) & 1) != 0;
                    ulong firstValue = *(ulong*)(*(int*)(address + 29) + address + 33);
                    ulong secondValue = *(ulong*)(*(int*)(address + 18) + address + 22);
                    var firstRol = *(byte*)(address + 25); // 0x1E
                    var secondRol = *(byte*)(address + 39); // 0x20
                    var andValue = *(byte*)(address + 42); // 0x1F
                    var addValue = *(byte*)(address + 45); // 2
                    s_fwScriptGuidPoolAddress = (ulong*)0UL;
                    if (isInitialized)
                    {
                        var rcx = secondValue;
                        rcx = Rol(rcx, firstRol);
                        var rdx = firstValue;
                        rdx = rdx ^ rcx;
                        rdx = Rol(rdx, secondRol);
                        var cl = (byte)(rcx & 0xFF);
                        cl = (byte)(cl & andValue);
                        cl = (byte)(cl + addValue);
                        rdx = Rol(rdx, cl);
                        rdx = ~rdx;
                        s_fwScriptGuidPoolAddress = (ulong*)rdx;
                    }
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x4C\x8B\x0D\x00\x00\x00\x00\x44\x8B\xC1\x49\x8B\x41\x08", "xxx????xxxxxxx");
                if (address != null)
                {
                    s_fwScriptGuidPoolAddress = (ulong*)(*(int*)(address + 3) + address + 7);
                }
            }
            
            address = s_isEnhanced ? MemScanner.FindPatternBmh("48 8b 05 ? ? ? ? 48 85 c0 74 ? 4c 8b 00") : MemScanner.FindPatternBmh("\x48\x8B\x05\x00\x00\x00\x00\xF3\x0F\x59\xF6\x48\x8B\x08", "xxx????xxxxxxx");
            if (address != null)
            {
                s_vehiclePoolAddress = (ulong*)(*(int*)(address + 3) + address + 7);
            }


            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 83 ec ? 89 ce 0f b6 05");
                if (address != null)
                {
                    bool isInitialized = (*(byte*)(*(int*)(address + 9) + address + 13) & 1) != 0;
                    ulong firstValue = *(ulong*)(*(int*)(address + 27) + address + 31);
                    ulong secondValue = *(ulong*)(*(int*)(address + 16) + address + 20);
                    var firstRol = *(byte*)(address + 23); // 0x1F
                    var secondRol = *(byte*)(address + 49); // 0x20
                    var andValue = *(byte*)(address + 36); // 0x1F
                    var addValue = *(byte*)(address + 39); // 5
                    s_pickupObjectPoolAddress = (ulong*)0UL;
                    if (isInitialized)
                    {
                        var rcx = secondValue;
                        rcx = Rol(rcx, firstRol);
                        var rax = firstValue;
                        rax = rax ^ rcx;
                        var cl = (byte)(rcx & 0xFF);
                        cl = (byte)(cl & andValue);
                        cl = (byte)(cl + addValue);
                        rax = Rol(rax, cl);
                        rax = Rol(rax, secondRol);
                        rax = ~rax;
                        s_pickupObjectPoolAddress = (ulong*)rax;
                    }
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x4C\x8B\x05\x00\x00\x00\x00\x40\x8A\xF2\x8B\xE9", "xxx????xxxxx");
                if (address != null)
                {
                    s_pickupObjectPoolAddress = (ulong*)(*(int*)(address + 3) + address + 7);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("45 84 f6 0f 84 ? ? ? ? 0f b6 05 ? ? ? ? 48");
                if (address != null)
                {
                    bool isInitialized = (*(byte*)(*(int*)(address + 12) + address + 16) & 1) != 0;
                    ulong firstValue = *(ulong*)(*(int*)(address + 30) + address + 34);
                    ulong secondValue = *(ulong*)(*(int*)(address + 19) + address + 23);
                    var firstRol = *(byte*)(address + 26); // 0x1b
                    var secondRol = *(byte*)(address + 40); // 0x20
                    var andValue = *(byte*)(address + 42); // 0x1F
                    var addValue = *(byte*)(address + 45); // 1
                    var xorValue = *(byte*)(address + 53); // 0x3f
                    s_pickupObjectPlacementPoolAddress = (ulong*)0UL;
                    if (isInitialized)
                    {
                        var rax = secondValue;
                        rax = Rol(rax, firstRol);
                        var rdx = firstValue;
                        rdx = rdx ^ rax;
                        rdx = Rol(rdx, secondRol);
                        var al = (byte)(rax & 0xFF);
                        al = (byte)(al & andValue);
                        rax = (rax & 0xFFFFFFFFFFFFFF00) | (ulong)al; // Updating rax since al changed
                        var ecx = (int)((rax + (ulong)addValue) & 0xFFFFFFFF); // LEA ECX, [RAX + 0x1]
                        var cl = (int)(ecx & 0xFF);
                        var rbp = rdx;
                        rbp = rbp << cl;
                        al = (byte)(al ^ xorValue);
                        rax = (rax & 0xFFFFFFFFFFFFFF00) | (ulong)al; // Updating rax since al changed
                        var eax = (int)(rax & 0xFFFFFFFF);
                        ecx = eax;
                        cl = (byte)(ecx & 0xFF);
                        rdx = rdx >> cl;
                        rdx = rdx | rbp;
                        rdx = ~rdx;
                        s_pickupObjectPlacementPoolAddress = (ulong*)rdx;
                    }
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("0f 84 ? ? ? ? 4c 8b 05 ? ? ? ? 49 63 78 ? 48 8b f7 eb");
                if (address != null)
                {
                    s_pickupObjectPlacementPoolAddress = (ulong*)(*(int*)(address + 9) + address + 13);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("f3 0f 10 58 ? f3 41 0f 5c 58");
                if (address != null)
                {
                    s_pickupObjectPlacementPositionOffset = (uint)(*(byte*)(address - 1));
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("f3 0f 10 42 ? f3 0f 5c 53");
                if (address != null)
                {
                    s_pickupObjectPlacementPositionOffset = (uint)(*(byte*)(address - 6));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("85 db 74 ? 0f b6 05 ? ? ? ? 48 8b 0d ? ? ? ? 48 c1 c1 ? 48 8b 05 ? ? ? ? 48 31 c8 80 e1");
                if (address != null)
                {
                    bool isInitialized = (*(byte*)(*(int*)(address + 7) + address + 11) & 1) != 0;
                    ulong firstValue = *(ulong*)(*(int*)(address + 25) + address + 29);
                    ulong secondValue = *(ulong*)(*(int*)(address + 14) + address + 18);
                    var firstRol = *(byte*)(address + 21); // 0x1f
                    var secondRol = *(byte*)(address + 44); // 0x20
                    var andValue = *(byte*)(address + 34); // 0x1f
                    var addValue = *(byte*)(address + 37); // 0x5
                    s_buildingPoolAddress = (ulong*)0UL;
                    if (isInitialized)
                    {
                        var rcx = secondValue;
                        rcx = Rol(rcx, firstRol);
                        var rax = firstValue;
                        rax = rax ^ rcx;
                        var cl = (byte)(rcx & 0xFF);
                        cl = (byte)(cl & andValue);
                        cl = (byte)(cl + addValue);
                        rax = Rol(rax, cl);
                        rax = Rol(rax, secondRol);
                        rax = ~rax;
                        s_buildingPoolAddress = (ulong*)rax;
                    }
                }
                address = MemScanner.FindPatternBmh("48 89 f2 e8 ? ? ? ? 48 89 f1 e8 ? ? ? ? 85 ff 74 ? 0f b6 05 ? ? ? ? 48 8b 0d");
                if (address != null)
                {
                    bool isInitialized = (*(byte*)(*(int*)(address + 23) + address + 27) & 1) != 0;
                    ulong firstValue = *(ulong*)(*(int*)(address + 41) + address + 45);
                    ulong secondValue = *(ulong*)(*(int*)(address + 30) + address + 34);
                    var firstRol = *(byte*)(address + 37); // 0x1d
                    var secondRol = *(byte*)(address + 51); // 0x20
                    var andValue = *(byte*)(address + 54); // 0x1f
                    var addValue = *(byte*)(address + 57); // 0x5
                    s_animatedBuildingPoolAddress = (ulong*)0UL;
                    if (isInitialized)
                    {
                        var rcx = secondValue;
                        rcx = Rol(rcx, firstRol);
                        var rax = firstValue;
                        rax = rax ^ rcx;
                        rax = Rol(rax, secondRol);
                        var cl = (byte)(rcx & 0xFF);
                        cl = (byte)(cl & andValue);
                        cl = (byte)(cl + addValue);
                        rax = Rol(rax, cl);
                        rax = ~rax;
                        s_animatedBuildingPoolAddress = (ulong*)rax;
                    }
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x83\x38\xFF\x74\x27\xD1\xEA\xF6\xC2\x01\x74\x20", "xxxxxxxxxxxx");
                if (address != null)
                {
                    s_buildingPoolAddress = (ulong*)(*(int*)(address + 47) + address + 51);
                    s_animatedBuildingPoolAddress = (ulong*)(*(int*)(address + 15) + address + 19);
                }
            }


            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 63 51 ? 48 63 f3");
                if (address != null)
                {
                    bool isInitialized = (*(byte*)(*(int*)(address - 77) + address - 73) & 1) != 0;
                    ulong firstValue = *(ulong*)(*(int*)(address - 59) + address - 55);
                    ulong secondValue = *(ulong*)(*(int*)(address - 70) + address - 66);
                    var firstRol = *(byte*)(address - 63); // 0x1c
                    var secondRol = *(byte*)(address - 49); // 0x20
                    var andValue = *(byte*)(address - 46); // 0x1f
                    var addValue = *(byte*)(address - 43); // 0x3
                    s_interiorInstPoolAddress = (ulong*)0UL;
                    if (isInitialized)
                    {
                        var rcx = secondValue;
                        rcx = Rol(rcx, firstRol);
                        var rax = firstValue;
                        rax = rax ^ rcx;
                        rax = Rol(rax, secondRol);
                        var cl = (byte)(rcx & 0xFF);
                        cl = (byte)(cl & andValue);
                        cl = (byte)(cl + addValue);
                        rax = Rol(rax, cl);
                        rax = ~rax;
                        s_interiorInstPoolAddress = (ulong*)rax;
                    }
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x83\xBB\x80\x01\x00\x00\x01\x75\x12", "xxxxxxxxx");
                if (address != null)
                {
                    s_interiorInstPoolAddress = (ulong*)(*(int*)(address + 23) + address + 27);
                }
            }


            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("0f 84 ? ? ? ? 48 8b 92 ? ? ? ? c7 44 24 20");
                if (address != null)
                {
                    bool isInitialized = (*(byte*)(*(int*)(address + 29) + address + 33) & 1) != 0;
                    ulong firstValue = *(ulong*)(*(int*)(address + 47) + address + 51);
                    ulong secondValue = *(ulong*)(*(int*)(address + 36) + address + 40);
                    var firstRol = *(byte*)(address + 43); // 0x1c
                    var secondRol = *(byte*)(address + 57); // 0x20
                    var andValue = *(byte*)(address + 60); // 0x1f
                    var addValue = *(byte*)(address + 63); // 0x3
                    s_interiorProxyPoolAddress = (ulong*)0UL;
                    if (isInitialized)
                    {
                        var rcx = secondValue;
                        rcx = Rol(rcx, firstRol);
                        var rax = firstValue;
                        rax = rax ^ rcx;
                        rax = Rol(rax, secondRol);
                        var cl = (byte)(rcx & 0xFF);
                        cl = (byte)(cl & andValue);
                        cl = (byte)(cl + addValue);
                        rax = Rol(rax, cl);
                        rax = ~rax;
                        s_interiorProxyPoolAddress = (ulong*)rax;
                    }
                }
            } else
            {
                address = MemScanner.FindPatternBmh("\x0F\x85\xA3\x00\x00\x00\x8B\x52\x0C\x48\x8B\x0D\x00\x00\x00\x00\xE8\x00\x00\x00\x00\x48\x85\xC0\x0F\x84\x8B\x00\x00\x00\x45\x33\xC9\x45\x33\xC0", "xxxxxxxxxxxx????x????xxxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_interiorProxyPoolAddress = (ulong*)(*(int*)(address + 12) + address + 16);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("0f 57 ff f3 0f 2a 38 84 db");
                if (address != null)
                {
                    s_physicalScrenWidthAddr = (int*)(*(int*)(address - 123) + address - 119);
                    s_physicalScrenHeightAddr = (int*)(*(int*)(address - 4) + address);
                    s_screenInfoAddr = new IntPtr((long*)(*(int*)(address + 55) + address + 59));

                    s_unkScreenFunc = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)((long*)(*(int*)(address + 63) + address + 67));
                    s_isUsingMultiScreenFunc = (delegate* unmanaged[Stdcall]<IntPtr, bool>)((long*)(*(int*)(address + 71) + address + 75));
                    s_getMainScreenInfoFunc = (delegate* unmanaged[Stdcall]<IntPtr, ScreenInfo*>)((long*)(*(int*)(address + 135) + address + 139));
                }
            } else
            {
                address = MemScanner.FindPatternBmh("\x0F\x84\x87\x00\x00\x00\xFF\xC9\x74\x79\xFF\xC9\x74\x6B\x66\x0F\x6E\x35", "xxxxxxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_physicalScrenWidthAddr = (int*)(*(int*)(address + 0x12) + address + 0x16);
                    s_physicalScrenHeightAddr = (int*)(*(int*)(address + 0x1A) + address + 0x1E);
                    s_screenInfoAddr = new IntPtr((long*)(*(int*)(address + 0x2B) + address + 0x2F));

                    s_unkScreenFunc = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)((long*)(*(int*)(address + 0x30) + address + 0x34));
                    s_isUsingMultiScreenFunc = (delegate* unmanaged[Stdcall]<IntPtr, bool>)((long*)(*(int*)(address + 0x38) + address + 0x3C));
                    s_getMainScreenInfoFunc = (delegate* unmanaged[Stdcall]<IntPtr, ScreenInfo*>)((long*)(*(int*)(address + 0x50) + address + 0x54));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("31 c0 80 7f ? ? 48 0f 45 f8 0f 85 ? ? ? ? e9");
                if (address != null)
                {
                    address = *(int*)(address + 17) + address + 21;
                    s_pedEntityPosSecondCheckOffset = *(int*)(address + 2);
                    address = *(int*)(address + 14) + address + 18;
                    s_pedEntityInVehicleCheckOffset = *(int*)(address + 3);
                } else
                {
                    s_pedEntityPosSecondCheckOffset = 0x144b; // Stable fallback.
                    s_pedEntityInVehicleCheckOffset = 0x1530; // Stable fallback.
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 89 d6 0f b6 42 ? 04 ? 3c ? 0f 87 ? ? ? ? e9");
                if (address != null)
                {
                    address = *(int*)(address + 13) + address + 17;
                    address = *(int*)(address + 1) + address + 5;
                    s_entityInternalTypeOffset = *(byte*)(address + 2);
                }
                else
                {
                    s_entityInternalTypeOffset = 0x28; // Stable fallback.
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("48 8b c1 48 85 c9 74 ? 80 79 ? 04 75 ?");
                if (address != null)
                {
                    s_entityInternalTypeOffset = *(byte*)(address + 10);
                }
                else
                {
                    s_entityInternalTypeOffset = 0x28; // Stable fallback.
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("0f 28 8f ? ? ? ? f3 0f 10 b7 ? ? ? ? f3 0f 16 c1 e9");
                if (address != null)
                {
                    s_entityPosFloatsOffset = *(int*)(address + 3);
                }
                else
                {
                    s_entityPosFloatsOffset = 0x90; // Stable fallback.
                }
            }
            // Find euphoria functions
            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("56 48 83 ec ? 48 89 ce 48 89 11 44 89 41");
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x40\x53\x48\x83\xEC\x20\x83\x61\x0C\x00\x44\x89\x41\x08\x49\x63\xC0", "xxxxxxxxxxxxxxxxx");
            }
            if (address != null)
            {
                s_initMessageMemoryFunc = (delegate* unmanaged[Stdcall]<ulong, ulong, int, ulong>)(new IntPtr(address));
            }


            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("44 8b 89 ? ? ? ? b9");
                if (address != null)
                {
                    s_sendNmMessageToPedFunc = (delegate* unmanaged[Stdcall]<ulong, IntPtr, ulong, void>)(new IntPtr(address));
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x0F\x84\x8B\x00\x00\x00\x48\x8B\x47\x30\x48\x8B\x48\x10\x48\x8B\x51\x20\x80\x7A\x10\x0A", "xxxxxxxxxxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_sendNmMessageToPedFunc = (delegate* unmanaged[Stdcall]<ulong, IntPtr, ulong, void>)((ulong*)(*(int*)(address - 0x1E) + address - 0x1A));
                }
            }

            if (s_isEnhanced)
            {
                //41 56 56 57 55 53 48 83 ec ? 48 89 ce 48 63 69 ? 3b 69 ? 7d ? 45 89 c6 48 89 d7
                address = MemScanner.FindPatternBmh("7d ? 45 89 c6 48 89 d7");
                if (address != null)
                {
                    s_setNmParameterInt = (delegate* unmanaged[Stdcall]<ulong, IntPtr, int, byte>)(new IntPtr(address - 20));
                }
            }
            else {
                address = MemScanner.FindPatternBmh("\x48\x89\x5C\x24\x00\x57\x48\x83\xEC\x20\x48\x8B\xD9\x48\x63\x49\x0C\x41\x8B\xF8", "xxxx?xxxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_setNmParameterInt = (delegate* unmanaged[Stdcall]<ulong, IntPtr, int, byte>)(new IntPtr(address));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("7d ? 45 89 c6 48 89 d3");
                if (address != null)
                {
                    s_setNmParameterBool = (delegate* unmanaged[Stdcall]<ulong, IntPtr, bool, byte>)(new IntPtr(address - 20));
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x89\x5C\x24\x00\x57\x48\x83\xEC\x20\x48\x8B\xD9\x48\x63\x49\x0C\x41\x8A\xF8", "xxxx?xxxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_setNmParameterBool = (delegate* unmanaged[Stdcall]<ulong, IntPtr, bool, byte>)(new IntPtr(address));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("41 56 56 57 53 48 83 ec ? 0f 29 74 24 20 48 89 ce 48 63 79");
            } else {
                address = MemScanner.FindPatternBmh("\x40\x53\x48\x83\xEC\x30\x48\x8B\xD9\x48\x63\x49\x0C", "xxxxxxxxxxxxx");
            }
            if (address != null)
            {
                s_setNmParameterFloat = (delegate* unmanaged[Stdcall]<ulong, IntPtr, float, byte>)(new IntPtr(address));
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("41 56 56 57 53 48 83 ec ? 48 89 ce 48 63 79");
                if (address != null)
                {
                    s_setNmParameterString = (delegate* unmanaged[Stdcall]<ulong, IntPtr, IntPtr, byte>)(new IntPtr(address));
                }
            } else {
                address = MemScanner.FindPatternBmh("\x57\x48\x83\xEC\x20\x48\x8B\xD9\x48\x63\x49\x0C\x49\x8B\xE8", "xxxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_setNmParameterString = (delegate* unmanaged[Stdcall]<ulong, IntPtr, IntPtr, byte>)(new IntPtr(address - 15));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("0f 29 7c 24 30 0f 29 74 24 20 48 89 ce 48 63 79");
                if (address != null)
                {
                    s_setNmParameterVector = (delegate* unmanaged[Stdcall]<ulong, IntPtr, float, float, float, byte>)(new IntPtr(address - 15));
                }
            } else
            {
                address = MemScanner.FindPatternBmh("\x40\x53\x48\x83\xEC\x40\x48\x8B\xD9\x48\x63\x49\x0C", "xxxxxxxxxxxxx");
                if (address != null)
                {
                    s_setNmParameterVector = (delegate* unmanaged[Stdcall]<ulong, IntPtr, float, float, float, byte>)(new IntPtr(address));
                }
            }


            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 8b 86 ? ? ? ? 48 8b 88 ? ? ? ? e8 ? ? ? ? 48 85 c0 0f 84 ? ? ? ? 48 89 c5");
                if (address != null)
                {
                    s_getActiveTaskFunc = (delegate* unmanaged[Stdcall]<ulong, CTask*>)(new IntPtr(*(int*)(address + 15) + address + 19));
                }
            } else {
                address = MemScanner.FindPatternBmh("\x4D\x8B\xF0\x48\x8B\xF2\xE8\x00\x00\x00\x00\x33\xFF\x48\x85\xC0\x75\x07\x32\xC0\xE9\xD8\x03\x00\x00", "xxxxxxx????xxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_getActiveTaskFunc = (delegate* unmanaged[Stdcall]<ulong, CTask*>)(new IntPtr(*(int*)(address + 7) + address + 11));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("0f b7 40 ? 41 b7 ? 3d ? ? ? ? 0f 84 ? ? ? ? e9");
            } else
            {
                address = MemScanner.FindPatternBmh("\x75\xEF\x48\x8B\x5C\x24\x30\xB8", "xxxxxxxx");
            }
            if (address != null)
            {
                s_cTaskNmScriptControlTypeIndex = *(int*)(address + 8);
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("80 bf ? ? ? ? ? 75 ? 48 8b 07 48 89 f9 48 89 da ff 50");
                if (address != null)
                {
                    s_getEventTypeIndexVFuncOffset = (uint)*(byte*)(address + 20);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x4C\x8B\x03\x48\x8B\xD5\x48\x8B\xCB\x41\xFF\x50\x00\x83\xFE\x04", "xxxxxxxxxxxx?xxx");
                if (address != null)
                {
                    // The instruction expects a signed value, but virtual function offsets can't be negative
                    s_getEventTypeIndexVFuncOffset = (uint)*(byte*)(address + 12);
                }
            }
            
            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 8d 0d ? ? ? ? 48 89 0e 89 46 ? 89 56 ? 4c 89 46");
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x8D\x05\x00\x00\x00\x00\x48\x89\x01\x8B\x44\x24\x50", "xxx????xxxxxxx");
            }
            if (address != null)
            {
                ulong cEventSwitch2NmVfTableArrayAddr = (ulong)(*(int*)(address + 3) + address + 7);
                ulong getEventTypeOfcEventSwitch2NmFuncAddr = *(ulong*)(cEventSwitch2NmVfTableArrayAddr + s_getEventTypeIndexVFuncOffset);
                // TODO: check if this is still the case for Enhanced.
                s_cEventSwitch2NmTypeIndex = *(int*)(getEventTypeOfcEventSwitch2NmFuncAddr + 1);
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("b3 ? f6 87 ? ? ? ? ? 74 ? 48 8b 47");
                if (address != null)
                {
                    s_fragInstNmGtaOffset = *(int*)(address + 23); // 0x1430
                }
            }
            else
            {
                address = MemScanner.FindPatternNaive("\x48\x83\xEC\x28\x48\x8B\x42\x00\x48\x85\xC0\x74\x09\x48\x3B\x82\x00\x00\x00\x00\x74\x21", "xxxxxxx?xxxxxxxx????xx");
                if (address != null)
                {
                    s_fragInstNmGtaOffset = *(int*)(address + 16); // 0x1430
                }
            }

            if (s_isEnhanced) {
                address = MemScanner.FindPatternBmh("b2 ? ff 90 ? ? ? ? 48 89 84 24 ? ? ? ? 80");
                if (address != null)
                {
                    s_fragInstNmGtaGetUnkValVFuncOffset = (uint)*(int*)(address + 4);
                }
            } else
            {
                address = MemScanner.FindPatternNaive("\xB2\x01\x48\x8B\x01\xFF\x90\x00\x00\x00\x00\x80", "xxxxxxx????x");
                if (address != null)
                {
                    s_fragInstNmGtaGetUnkValVFuncOffset = (uint)*(int*)(address + 7);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 8d 0d ? ? ? ? 4c 89 f2 e8 ? ? ? ? 8b 17");
                if (address != null)
                {
                    s_getLabelTextByHashAddress = (ulong)(*(int*)(address + 3) + address + 7);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x84\xC0\x74\x34\x48\x8D\x0D\x00\x00\x00\x00\x48\x8B\xD3", "xxxxxxx????xxx");
                if (address != null)
                {
                    s_getLabelTextByHashAddress = (ulong)(*(int*)(address + 7) + address + 11);
                }
            }



            // Find the function that returns if the corresponding text label exist first.
            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("74 ? 48 8d 0d ? ? ? ? 48 8d 15 ? ? ? ? e8 ? ? ? ? 84 c0 0f 84");
                if (address != null)
                {
                    byte* doesTextLabelExistFuncAddr = (byte*)(*(int*)(address + 17) + address + 21);
                    long getLabelTextByHashFuncAddr = (long)(*(int*)(doesTextLabelExistFuncAddr + 24) + doesTextLabelExistFuncAddr + 28);
                    s_getLabelTextByHashFunc = (delegate* unmanaged[Stdcall]<ulong, int, ulong>)(new IntPtr(getLabelTextByHashFuncAddr));
                }
            }
            else
            {
                // We have to find GetLabelTextByHashFunc indirectly since Rampage Trainer hooks the function that returns the string address for corresponding text label hash by inserting jmp instruction at the beginning if that trainer is installed.
                address = MemScanner.FindPatternBmh("\x74\x64\x48\x8D\x15\x00\x00\x00\x00\x48\x8D\x0D\x00\x00\x00\x00\xE8\x00\x00\x00\x00\x84\xC0\x74\x33", "xxxxx????xxx????x????xxxx");
                if (address != null)
                {
                    byte* doesTextLabelExistFuncAddr = (byte*)(*(int*)(address + 17) + address + 21);
                    long getLabelTextByHashFuncAddr = (long)(*(int*)(doesTextLabelExistFuncAddr + 28) + doesTextLabelExistFuncAddr + 32);
                    s_getLabelTextByHashFunc = (delegate* unmanaged[Stdcall]<ulong, int, ulong>)(new IntPtr(getLabelTextByHashFuncAddr));
                }
            }


            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("83 fe ? 75 ? b8 ? ? ? ? 48 8d 0d");
                if (address != null)
                {
                    s_checkpointPoolAddress = (ulong*)(*(int*)(address + 13) + address + 17);
                }
                address = MemScanner.FindPatternBmh("48 83 ec ? e8 ? ? ? ? 48 85 c0 74 ? e8 ? ? ? ? 48 8b 80");
                if (address != null)
                {
                    s_getCGameScriptHandlerAddressFunc = (delegate* unmanaged[Stdcall]<ulong>)(new IntPtr(address));
                }
            } else
            {
                address = MemScanner.FindPatternBmh("\x8A\x4C\x24\x60\x8B\x50\x10\x44\x8A\xCE", "xxxxxxxxxx");
                if (address != null)
                {
                    s_checkpointPoolAddress = (ulong*)(*(int*)(address + 17) + address + 21);
                    s_getCGameScriptHandlerAddressFunc = (delegate* unmanaged[Stdcall]<ulong>)(new IntPtr(*(int*)(address - 19) + address - 15));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("49 63 f4 48 8d 3d");
                if (address != null)
                {
                    s_radarBlipPoolAddress = (ulong*)(*(int*)(address + 6) + address + 10);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x4C\x8D\x05\x00\x00\x00\x00\x0F\xB7\xC1", "xxx????xxx");
                if (address != null)
                {
                    s_radarBlipPoolAddress = (ulong*)(*(int*)(address + 3) + address + 7);
                }
            }

            if (s_isEnhanced) {
                address = MemScanner.FindPatternBmh("8b 0d ? ? ? ? 4c 63 c1");
                if (address != null)
                {
                    s_possibleRadarBlipCountAddress = (int*)(*(int*)(address - 12) + address - 8);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\xFF\xC6\x49\x83\xC6\x08\x3B\x35\x00\x00\x00\x00\x7C\x9B", "xxxxxxxx????xx");
                if (address != null)
                {
                    s_possibleRadarBlipCountAddress = (int*)(*(int*)(address + 8) + address + 12);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 63 0d ? ? ? ? 49 8b 0c ce 38 41");
                if (address != null)
                {
                    s_unkFirstRadarBlipIndexAddress = (int*)(*(int*)(address + 3) + address + 7);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x8B\x44\x0A\x20\x89\x01\x48\x8D\x49\x04\x49\xFF\xC8\x75\xF1\xF3\xC3\x48\x63\x05", "xxxxxxxxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_unkFirstRadarBlipIndexAddress = (int*)(*(int*)(address + 20) + address + 24);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("b8 ff ff ff ff 80 3d ? ? ? ? 00 74 ? 8b 15");
                if (address != null)
                {
                    s_northRadarBlipHandleAddress = (int*)(*(int*)(address + 16) + address + 20);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x41\xB8\x07\x00\x00\x00\x8B\xD0\x89\x05\x00\x00\x00\x00\x41\x8D\x48\xFC", "xxxxxxxxxx????xxxx");
                if (address != null)
                {
                    s_northRadarBlipHandleAddress = (int*)(*(int*)(address + 10) + address + 14);
                }
            }
            

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 89 c6 8b 05 ? ? ? ? 85 c0");
                if (address != null)
                {
                    s_centerRadarBlipHandleAddress = (int*)(*(int*)(address + 5) + address + 9);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x41\xB8\x06\x00\x00\x00\x8B\xD0\x89\x05\x00\x00\x00\x00\x41\x8D\x48\xFD", "xxxxxxxxxx????xxxx");
                if (address != null)
                {
                    s_centerRadarBlipHandleAddress = (int*)(*(int*)(address + 10) + address + 14);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("e8 ? ? ? ? 48 8b b8 ? ? ? ? 48 8d 0d");
                if (address != null)
                {
                    s_getLocalPlayerPedAddressFunc = (delegate* unmanaged[Stdcall]<ulong>)(new IntPtr(*(int*)(address + 1) + address + 5));
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x33\xDB\xE8\x00\x00\x00\x00\x48\x85\xC0\x74\x07\x48\x8B\x40\x20\x8B\x58\x18", "xxx????xxxxxxxxxxxx");
                if (address != null)
                {
                    s_getLocalPlayerPedAddressFunc = (delegate* unmanaged[Stdcall]<ulong>)(new IntPtr(*(int*)(address + 3) + address + 7));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 8d 15 ? ? ? ? 83 7c c2");
                if (address != null)
                {
                    // practically s_waypointInfoArrayAddress0
                    s_waypointInfoArrayStartAddress = (ulong*)(*(int*)(address + 3) + address + 7);
                    s_waypointInfoArrayAddresses[0] = (ulong)s_waypointInfoArrayStartAddress;
                }
                address = MemScanner.FindPatternBmh("39 0d ? ? ? ? 74 9f");
                if (address != null)
                {
                    // Enhanced doesn't use an arrayEndAddress, but an address for each element (4).
                    s_waypointInfoArrayAddress1 = (ulong*)(*(int*)(address + 2) + address + 6);
                    s_waypointInfoArrayAddresses[1] = (ulong)s_waypointInfoArrayAddress1;
                    s_waypointInfoArrayAddress2 = (ulong*)(*(int*)(address + 15) + address + 19);
                    s_waypointInfoArrayAddresses[2] = (ulong)s_waypointInfoArrayAddress2;
                    s_waypointInfoArrayAddress3 = (ulong*)(*(int*)(address + 28) + address + 32);
                    s_waypointInfoArrayAddresses[3] = (ulong)s_waypointInfoArrayAddress3;
                }

            } else
            {
                address = MemScanner.FindPatternBmh("\x4C\x8D\x05\x00\x00\x00\x00\x74\x07\xB8\x00\x00\x00\x00\xEB\x2D\x33\xC0", "xxx????xxx????xxxx");
                if (address != null)
                {
                    s_waypointInfoArrayStartAddress = (ulong*)(*(int*)(address + 3) + address + 7);

                    startAddressToSearch = new IntPtr(address);
                    address = MemScanner.FindPatternBmh("\x48\x8D\x15\x00\x00\x00\x00\x48\x83\xC1\x00\xFF\xC0\x48\x3B\xCA\x7C\xEA\x32\xC0", "xxx????xxx?xxxxxxxxx", startAddressToSearch);
                    s_waypointInfoArrayEndAddress = (ulong*)(*(int*)(address + 3) + address + 7);
                }
            }


            if (s_isEnhanced)
            {
                // No unique pattern could be found in Enhanced, as the function and its callers are too short.
                // Instead, we find a unique pattern for a called function, and check which of the dozen matches for our pattern calls it. 

                byte* calledFunctionAddress = MemScanner.FindPatternBmh("48 8b 41 ? 8b 70 ? 48 8b 10 48 8b 3d ? ? ? ? 31 c9 e9");
                if (calledFunctionAddress != null)
                {
                    string pattern = "56 57 48 83 ec 28 80 3d ? ? ? ? 00 0f 85 ? ? ? ? e9";
                    address = MemScanner.FindPatternBmh(pattern);
                    while (address != null)
                    {
                        if ((ulong)(*(int*)(address + 20) + address + 24) == (ulong)calledFunctionAddress)
                        {
                            break;
                        }
                        address = MemScanner.FindPatternBmh(pattern, new IntPtr((long)((ulong)address + 20)));
                    }
                    if (address != null)
                    {
                        s_isDecoratorLocked = (byte*)(*(int*)(address + 8) + address + 13);
                    }
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x80\x3D\x00\x00\x00\x00\x00\x8B\xDA\x75\x29\x48\x8B\xD1\x33\xC9\xE8", "xx????xxxxxxxxxxx");
                if (address != null)
                {
                    s_isDecoratorLocked = (byte*)(*(int*)(address + 2) + address + 7);
                }
            }
            

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("56 48 83 ec ? 48 89 ce 41 83 f8 ? 77");
                if (address != null)
                {
                    s_getRotationFromMatrixFunc = (delegate* unmanaged[Stdcall]<float*, ulong, int, float*>)(new IntPtr(address));
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\xF3\x0F\x10\x5C\x24\x20\xF3\x0F\x10\x54\x24\x24\xF3\x0F\x59\xD9\xF3\x0F\x59\xD1\xF3\x0F\x10\x44\x24\x28\xF3\x0F\x11\x1F", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_getRotationFromMatrixFunc = (delegate* unmanaged[Stdcall]<float*, ulong, int, float*>)(new IntPtr(*(int*)(address - 0x14) + address - 0x10));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("56 57 48 83 ec ? 48 89 d7 48 89 ce f3 0f 10 0a");
                if (address != null)
                {
                    s_getQuaternionFromMatrixFunc = (delegate* unmanaged[Stdcall]<float*, ulong, int>)(new IntPtr(address));
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\xF3\x0F\x11\x4D\x38\xF3\x0F\x11\x45\x3C\xE8\x00\x00\x00\x00\x0F\x28\xC6\x0F\x28\xCE\xB9\x01\x00\x00\x00\xF3\x0F\x11\x73\x10\x66\x44\x03\xE9", "xxxxxxxxxxx????xxxxxxxxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_getQuaternionFromMatrixFunc = (delegate* unmanaged[Stdcall]<float*, ulong, int>)(new IntPtr(*(int*)(address + 11) + address + 15));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("f3 0f 10 8e ? ? ? ? 0f 2e c8 0f 85 ? ? ? ? e9");
                if (address != null)
                {
                    EntityMaxHealthOffset = *(int*)(address + 4);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x8B\x42\x20\x48\x85\xC0\x74\x09\xF3\x0F\x10\x80", "xxxxxxxxxxxxx");
                if (address != null)
                {
                    EntityMaxHealthOffset = *(int*)(address + 0x25);
                }
            }

            if (s_isEnhanced)
            {
                // startAddressToSearch points inside the function containing the second pattern.
                // Although it is possible to make the second pattern unique, that would require adding bytes from before the Label (jump target).
                // Labels could be rearranged by compiler optimizations in future updates (even if rare), so this approach should offer more stability.
                startAddressToSearch = new IntPtr(MemScanner.FindPatternBmh("0f 29 74 24 ? 48 89 d6 48 89 cb 48 8b 81"));
                address = MemScanner.FindPatternBmh("48 89 f9 ff 90", startAddressToSearch);
                if (address != null)
                {
                    SetAngularVelocityVFuncOfEntityOffset = *(int*)(address + 5);
                    GetAngularVelocityVFuncOfEntityOffset = SetAngularVelocityVFuncOfEntityOffset + 0x8;
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x75\x11\x48\x8B\x06\x48\x8D\x54\x24\x20\x48\x8B\xCE\xFF\x90", "xxxxxxxxxxxxxxx");
                if (address != null)
                {
                    SetAngularVelocityVFuncOfEntityOffset = *(int*)(address + 15);
                    GetAngularVelocityVFuncOfEntityOffset = SetAngularVelocityVFuncOfEntityOffset + 0x8;
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("41 56 56 57 55 53 48 83 ec ? 48 89 cf 48 8b 89");
                if (address != null)
                {
                    NativeMemory.CAttackerArrayOfEntityOffset = *(int*)(address + 16); // the correct name is unknown

                    startAddressToSearch = new IntPtr(address);
                    address = MemScanner.FindPatternBmh("48 63 51 48 48 83 c3 18", startAddressToSearch);
                    NativeMemory.ElementCountOfCAttackerArrayOfEntityOffset = (*(sbyte*)(address + 3));
                    NativeMemory.ElementSizeOfCAttackerArrayOfEntity = (*(sbyte*)(address + 7));
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x8B\x89\x00\x00\x00\x00\x33\xC0\x44\x8B\xC2\x48\x85\xC9\x74\x20", "xxx????xxxxxxxxxx");
                if (address != null)
                {
                    NativeMemory.CAttackerArrayOfEntityOffset = *(int*)(address + 3); // the correct name is unknown

                    startAddressToSearch = new IntPtr(address);
                    address = MemScanner.FindPatternBmh("\x48\x63\x51\x00\x48\x85\xD2", "xxx?xxx", startAddressToSearch);
                    NativeMemory.ElementCountOfCAttackerArrayOfEntityOffset = (*(sbyte*)(address + 3));

                    startAddressToSearch = new IntPtr(address);
                    address = MemScanner.FindPatternBmh("\x48\x83\xC1\x00\x48\x3B\xC2\x7C\xEF", "xxx?xxxxx", startAddressToSearch);
                    // the element size might be 0x10 in older builds (the size is 0x18 at least in b1604 and b2372)
                    NativeMemory.ElementSizeOfCAttackerArrayOfEntity = (*(sbyte*)(address + 3));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("89 0d ? ? ? ? 48 8d 0d ? ? ? ? e8 ? ? ? ? 84 c0");
                if (address != null)
                {
                    s_cursorSpriteAddr = (int*)(*(int*)(address + 2) + address + 6);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x74\x11\x8B\xD1\x48\x8D\x0D\x00\x00\x00\x00\x45\x33\xC0", "xxxxxxx????xxx");
                if (address != null)
                {
                    s_cursorSpriteAddr = (int*)(*(int*)(address - 4) + address);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("89 c8 48 8d 0d ? ? ? ? f3 0f 10 04 81 f3 0f 11 05");
                if (address != null)
                {
                    s_readWorldGravityAddress = (float*)(*(int*)(address + 18) + address + 22);
                    s_writeWorldGravityAddress = (float*)(*(int*)(address + 5) + address + 9);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x63\xC1\x48\x8D\x0D\x00\x00\x00\x00\xF3\x0F\x10\x04\x81\xF3\x0F\x11\x05", "xxxxxx????xxxxxxxxx");
                if (address != null)
                {
                    s_readWorldGravityAddress = (float*)(*(int*)(address + 19) + address + 23);
                    s_writeWorldGravityAddress = (float*)(*(int*)(address + 6) + address + 10);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 8b 41 ? f3 0f 10 00 f3 0f 11 05 ? ? ? ? f3 0f 5d 05");
                if (address != null)
                {
                    // Here we determine it directly unlike in legacy.
                    // If the address of the array is ever needed, it can be found at address+20 or s_timeScaleAddress-1.
                    s_timeScaleAddress = (float*)(*(int*)(address + 12) + address + 16);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\xF3\x0F\x11\x05\x00\x00\x00\x00\xF3\x0F\x10\x08\x0F\x2F\xC8\x73\x03\x0F\x28\xC1\x48\x83\xC0\x04\x49\x2B", "xxxx????xxxxxxxxxxxxxxxxxx");
                if (address != null)
                {
                    float* timeScaleArrayAddress = (float*)(*(int*)(address + 4) + address + 8);
                    // SET_TIME_SCALE changes the 2nd element, so obtain the address of it
                    s_timeScaleAddress = timeScaleArrayAddress + 1;
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("f3 0f 2a 0d ? ? ? ? f3 0f 5e 05 ? ? ? ? f3 0f 59 c1");
                if (address != null)
                {
                    s_millisecondsPerGameMinuteAddress = (int*)(*(int*)(address + 4) + address + 8);
                }
                address = MemScanner.FindPatternBmh("48 8b 05 ? ? ? ? 8b 68 ? c1 e5");
                if (address != null)
                {
                    s_lastClockTickAddress = (int*)(*(int*)(address + 3) + address + 7);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\xF3\x0F\x11\xB5\x60\x01\x00\x00\x84\xC0\x75\x4C\x85\xC9\x79\x1D\x33\xD2\xE8", "xxxxxxxxxxxxxxxxxxx");
                if (address != null)
                {
                    byte* unkClockFunc = (byte*)(*(int*)(address + 19) + address + 23);
                    s_millisecondsPerGameMinuteAddress = (int*)(*(int*)(unkClockFunc + 0x46) + unkClockFunc + 0x4A);
                }
                address = MemScanner.FindPatternBmh("48 8b 05 ? ? ? ? 48 8b d1 8b 58");
                if (address != null)
                {
                    // Getting the address through a pattern should be more robust than hardcoding an offset.
                    s_lastClockTickAddress = (int*)(*(int*)(address + 3) + address + 7);
                }
                else
                {
                    if (s_millisecondsPerGameMinuteAddress != null)
                    {
                        s_lastClockTickAddress = (int*)(s_millisecondsPerGameMinuteAddress + 2); // Stable fallback.
                    }
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("41 0f 95 c0 0f b6 05");
                if (address != null)
                {
                    s_isClockPausedAddress = (byte*)(*(int*)(address - 12) + address - 8);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x75\x2D\x44\x38\x3D\x00\x00\x00\x00\x75\x24", "xxxxx????xx");
                if (address != null)
                {
                    s_isClockPausedAddress = (byte*)(*(int*)(address + 5) + address + 9);
                }
            }

            // Find camera objects
            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 89 cb 48 8b 41 ? 8b 10 f2 0f 10 3d");
                if (address != null)
                {
                    // The pattern is inside a function, which calls a function, which accesses the pool.
                    address = (byte*)(*(int*)(address + 34) + address + 38);
                    if (address != null)
                    {
                        bool isInitialized = (*(byte*)(*(int*)(address + 3) + address + 7) & 1) != 0;
                        ulong firstValue = *(ulong*)(*(int*)(address + 21) + address + 25);
                        ulong secondValue = *(ulong*)(*(int*)(address + 10) + address + 14);
                        var firstRol = *(byte*)(address + 17); // 0x1b
                        var secondRol = *(byte*)(address + 31); // 0x20
                        var andValue = *(byte*)(address + 33); // 0x1f
                        var addValue = *(byte*)(address + 36); // 0x1
                        var xorValue = *(byte*)(address + 44); // 0x3f
                        s_cameraPoolAddress = (ulong*)0UL;
                        if (isInitialized)
                        {
                            var rax = secondValue;
                            rax = Rol(rax, firstRol);
                            var rsi = firstValue;
                            rsi = rsi ^ rax;
                            rsi = Rol(rsi, secondRol);
                            var al = (byte)(rax & 0xFF);
                            al = (byte)(al & andValue);
                            rax = (rax & 0xFFFFFFFFFFFFFF00UL) | (ulong)al;
                            var ecx = (int)((rax + (ulong)addValue) & 0xFFFFFFFF);
                            var rdi = rsi;
                            var cl = (byte)(ecx & 0xFF);
                            rdi = rdi << cl;
                            al = (byte)(al ^ xorValue);
                            rax = (rax & 0xFFFFFFFFFFFFFF00UL) | (ulong)al;
                            var eax = (int)(rax & 0xFFFFFFFF);
                            ecx = eax;
                            cl = (byte)(ecx & 0xFF);
                            rsi = rsi >> cl;
                            rsi = rsi | rdi;
                            rsi = ~rsi;
                            s_cameraPoolAddress = (ulong*)rsi;
                        }
                    }
                    
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x8B\xC8\xEB\x02\x33\xC9\x48\x85\xC9\x74\x26", "xxxxxxxxxxxx");
                if (address != null)
                {
                    s_cameraPoolAddress = (ulong*)(*(int*)(address - 9) + address - 5);
                }
            }
            
            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("88 05 ? ? ? ? e8 ? ? ? ? 88 05 ? ? ? ? e8");
                if (address != null)
                {
                    address = (*(int*)(address + 18) + address + 22);
                    s_gameplayCameraAddress = (ulong*)(*(int*)(address + 3) + address + 7);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x8B\xC7\xF3\x0F\x10\x0D", "xxxxxxx");
                if (address != null)
                {
                    address = (*(int*)(address - 0x1D) + address - 0x19);
                    s_gameplayCameraAddress = (ulong*)(*(int*)(address + 3) + address + 7);
                }
            }

            // Find model hash table

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("8b 80 ? ? ? ? 83 c0 ? 31 c9 83 f8 ? 0f 92 c1 e9");
                if (address != null)
                {
                    s_vehicleTypeOffsetInModelInfo = *(int*)(address + 2);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x3C\x05\x75\x16\x8B\x81", "xxxxxx");
                if (address != null)
                {
                    s_vehicleTypeOffsetInModelInfo = *(int*)(address + 6);
                }
            }

            uint vehicleClassOffset = 0;
            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("0f b6 88 ? ? ? ? 83 e1 ? e9");
                if (address != null)
                {
                    vehicleClassOffset = *(uint*)(address + 3);

                    address = MemScanner.FindPatternBmh("74 ? 49 89 d0 4c 8b 1d");
                    if (address != null)
                    {
                        s_modelHashTable = *(UInt64*)(*(int*)(address + 8) + address + 12);
                        s_modelHashEntries = *(UInt16*)(address + *(int*)(address - 7) - 3);
                        // Pattern scan to avoid having offsets accross labels.
                        address = MemScanner.FindPatternBmh("3b 05 ? ? ? ? 7d ? 48 8b 0d", new IntPtr(address));
                        s_modelNum1 = *(UInt32*)(*(int*)(address + 2) + address + 6);
                        s_modelNum2 = *(UInt64*)(*(int*)(address + 11) + address + 15);
                        s_modelNum3 = *(UInt64*)(*(int*)(address + 48) + address + 52);
                        s_modelNum4 = *(UInt64*)(*(int*)(address + 33) + address + 37);
                    }
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x66\x81\xF9\x00\x00\x74\x10\x4D\x85\xC0", "xxx??xxxxx");
                if (address != null)
                {
                    vehicleClassOffset = *(uint*)(address + 0x10);

                    address = (*(int*)(address - 0x21) + address - 0x1D);
                    s_modelNum1 = *(UInt32*)(*(int*)(address + 0x52) + address + 0x56);
                    s_modelNum2 = *(UInt64*)(*(int*)(address + 0x63) + address + 0x67);
                    s_modelNum3 = *(UInt64*)(*(int*)(address + 0x7A) + address + 0x7E);
                    s_modelNum4 = *(UInt64*)(*(int*)(address + 0x81) + address + 0x85);
                    s_modelHashTable = *(UInt64*)(*(int*)(address + 0x24) + address + 0x28);
                    s_modelHashEntries = *(UInt16*)(address + *(int*)(address + 3) + 7);
                }
            }


            // This is the same as s_modelNum4. I found a different pattern anyway, just in case finding s_modelNum4 fails.
            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("83 b8 ? ? ? ? ? 75 ? 80 bf");
                if (address != null)
                {
                    s_modelInfoArrayPtr = (ulong*)(*(int*)(address - 8) + address - 4);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x33\xD2\x00\x8B\xD0\x00\x2B\x05\x00\x00\x00\x00\xC1\xE6\x10", "xx?xx?xx????xxx");
                if (address != null)
                {
                    s_modelInfoArrayPtr = (ulong*)(*(int*)(address + 8) + address + 12);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 8d 0d ? ? ? ? 31 d2 45 31 c0 e8 ? ? ? ? 84 c0 74 ? 48 8d 0d");
                if (address != null)
                {
                    s_cStreamingAddr = (ulong*)(*(int*)(address + 3) + address + 7);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x83\xEC\x20\x48\x8B\x91\x00\x00\x00\x00\x33\xF6\x48\x8B\xD9\x48\x85\xD2\x74\x2B\x48\x8D\x0D", "xxxxxxx??xxxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_cStreamingAddr = (ulong*)(*(int*)(address + 24) + address + 28);
                }
            }

            // 0x3600 could be used as a stable fallback offset
            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 8d 91 ? ? ? ? 48 89 44 24 ? 48 89 cf");
                if (address != null)
                {
                    // The unkFunc in Legacy was inlined in Enhanced, so we find the offset directly.
                    s_cStreamingAppropriateVehicleIndicesOffset = *(int*)(address - 18);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x44\x39\x38\x74\x17\x48\xFF\xC1\x48\x83\xC0\x04\x48\x3B\xCB\x7C\xEF\x41\x8B\xD7\x49\x8B\xCE\xE8", "xxxxxxxxxxxxxxxxxxxxxxxx");
                if (address != null)
                {
                    var unkFuncForVehicleModelIndices = (byte*)(*(int*)(address + 0x18) + address + 0x1C);
                    s_cStreamingAppropriateVehicleIndicesOffset = *(int*)(unkFuncForVehicleModelIndices + 0x1E);
                }
            }

            // 0x4e04 could be used as a stable fallback offset
            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 8b 54 24 ? 48 8d 82");
                if (address != null)
                {
                    // The unkFunc in Legacy was inlined in Enhanced, so we find the offset directly.
                    s_cStreamingAppropriatePedIndicesOffset = *(int*)(address + 8);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x75\x0D\x8B\xD7\x49\x8B\xCE\xE8\x00\x00\x00\x00\x41\x2B\xDD\x45\x03\xFD\x41\x03\xDD\x41\x3B\xDC\x0F\x8C\x9A\xFE\xFF\xFF", "xxxxxxxx????xxxxxxxxxxxxxxxxxx");
                if (address != null)
                {
                    var unkFuncForPedModelIndices = (byte*)(*(int*)(address + 8) + address + 12);
                    s_cStreamingAppropriatePedIndicesOffset = *(int*)(unkFuncForPedModelIndices + 0x1E);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("ff ce 31 c9 4c 8b 05");
                if (address != null)
                {
                    s_weaponAndAmmoInfoArrayPtr = (RageAtArrayPtr*)(*(int*)(address + 7) + address + 11);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x8B\x05\x00\x00\x00\x00\x41\x8B\x1E", "xxx????xxx");
                if (address != null)
                {
                    s_weaponAndAmmoInfoArrayPtr = (RageAtArrayPtr*)(*(int*)(address + 3) + address + 7);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("8b 91 ? ? ? ? 39 d0 75 ? eb");
                if (address != null)
                {
                    s_weaponInfoHumanNameHashOffset = *(int*)(address + 2);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x84\xC0\x74\x20\x48\x8B\x47\x40\x48\x85\xC0\x74\x08\x8B\xB0\x00\x00\x00\x00\xEB\x02\x33\xF6\x48\x8D\x4D\x48\xE8", "xxxxxxxxxxxxxxx????xxxxxxxxx");
                if (address != null)
                {
                    s_weaponInfoHumanNameHashOffset = *(int*)(address + 15);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("44 8B 09 45 85 C9 74 ? 44 8B 15");
                if (address != null)
                {
                    s_weaponComponentArrayCountAddr = (uint*)(*(int*)(address + 11) + address + 15);
                    s_offsetForCWeaponComponentArrayAddr = (ulong)(address + 26);

                    address = MemScanner.FindPatternBmh("56 57 44 8b 89 ? ? ? ? 45 85 c9");
                    if (address != null)
                    {
                        var findAttachPointFuncAddr = new IntPtr((long)address);

                        // 0x8f8
                        address = MemScanner.FindPatternBmh("44 8b 89");
                        int attachPointsStructsCountOffset = *(int*)(address + 3);

                        // 0x604
                        address = MemScanner.FindPatternBmh("4c 8d 81", new IntPtr(address));
                        s_weaponAttachPointsStartOffset = *(int*)(address + 3);

                        // 0x8f8 - 0x604 = 0x2f4
                        s_weaponAttachPointsArrayCountOffset = attachPointsStructsCountOffset - s_weaponAttachPointsStartOffset;

                        // 0x6c
                        address = MemScanner.FindPatternBmh("49 6b fa", new IntPtr(address));
                        s_weaponAttachPointElementSize = *(byte*)(address + 3);

                        // 0x68
                        s_weaponAttachPointElementComponentCountOffset = *(byte*)(address + 8);
                    }
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x8B\x05\x00\x00\x00\x00\x44\x8B\xD3\x8D\x48\xFF", "xx????xxxxxx");
                if (address != null)
                {
                    s_weaponComponentArrayCountAddr = (uint*)(*(int*)(address + 2) + address + 6);

                    address = MemScanner.FindPatternNaive("\x46\x8D\x04\x11\x48\x8D\x15\x00\x00\x00\x00\x41\xD1\xF8", "xxxxxxx????xxx", new IntPtr(address));
                    s_offsetForCWeaponComponentArrayAddr = (ulong)(address + 7);

                    address = MemScanner.FindPatternNaive("\x74\x10\x49\x8B\xC9\xE8", "xxxxxx", new IntPtr(address));
                    var findAttachPointFuncAddr = new IntPtr((long)(*(int*)(address + 6) + address + 10));

                    // 0x604
                    address = MemScanner.FindPatternNaive("\x4C\x8D\x81", "xxx", findAttachPointFuncAddr);
                    s_weaponAttachPointsStartOffset = *(int*)(address + 3);

                    // 0x2f4
                    address = MemScanner.FindPatternNaive("\x4D\x63\x98", "xxx", new IntPtr(address));
                    s_weaponAttachPointsArrayCountOffset = *(int*)(address + 3);

                    // 0x60
                    address = MemScanner.FindPatternNaive("\x4C\x63\x50", "xxx", new IntPtr(address));
                    s_weaponAttachPointElementComponentCountOffset = *(byte*)(address + 3);

                    // 0x6c
                    address = MemScanner.FindPatternNaive("\x48\x83\xC0", "xxx", new IntPtr(address));
                    s_weaponAttachPointElementSize = *(byte*)(address + 3);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 8d 9d ? ? ? ? 41 80 bc 24");
                if (address != null)
                {
                    s_vehicleMakeNameOffsetInModelInfo = *(int*)(address + 11);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x24\x1F\x3C\x05\x0F\x85\x00\x00\x00\x00\x48\x8D\x82\x00\x00\x00\x00\x48\x8D\xB2\x00\x00\x00\x00\x48\x85\xC0\x74\x09\x80\x38\x00\x74\x04\x8A\xCB", "xxxxxx????xxx????xxx????xxxxxxxxxxxx");
                if (address != null)
                {
                    s_vehicleMakeNameOffsetInModelInfo = *(int*)(address + 13);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 69 c0 ? ? ? ? 0f b6 74 01");
                if (address != null)
                {
                    s_pedPersonalityIndexOffsetInModelInfo = *(int*)(address - 13);
                    s_pedPersonalitiesArrayAddr = (ulong*)(*(int*)(address - 6) + address - 2);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x66\x89\x44\x24\x38\x8B\x44\x24\x38\x8B\xC8\x33\x4C\x24\x30\x81\xE1\x00\x00\xFF\x0F\x33\xC1\x0F\xBA\xF0\x1D\x8B\xC8\x33\x4C\x24\x30\x23\xCB\x33\xC1", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
                if (address != null)
                {
                    s_pedPersonalityIndexOffsetInModelInfo = *(int*)(address + 0x42);
                    s_pedPersonalitiesArrayAddr = (ulong*)(*(int*)(address + 0x49) + address + 0x4D);
                }
            }


            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("ff 50 ? 84 c0 74 ? f6 87 ? ? ? ? ? 75 ? 48 8b 8f");
                if (address != null)
                {
                    int pedIntelligenceOffset = *(int*)(address + 18);
                    PedPlayerInfoOffset = pedIntelligenceOffset + 0x8; // TODO: verify this.
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x85\xC0\x74\x7F\xF6\x80\x00\x00\x00\x00\x02\x75\x76", "xxxxxxx????xxx");
                if (address != null)
                {
                    int pedIntelligenceOffset = *(int*)(address + 0x11);
                    PedPlayerInfoOffset = pedIntelligenceOffset + 0x8;
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 8b 80 ? ? ? ? 0f b7 80 ? ? ? ? 0f 57 c9");
                if (address != null)
                {
                    CPlayerInfoMaxHealthOffset = *(int*)(address + 10);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x66\x0F\x6E\xF0\x0F\x5B\xF6\xE8\x00\x00\x00\x00\x0F\x28\xCE\x41\xB1\x01\x45\x33\xC0\x48\x8B\xC8\xE8", "xxxxxxxx????xxxxxxxxxxxxx");
                if (address != null)
                {
                    CPlayerInfoMaxHealthOffset = *(int*)(address - 4);
                }
            }

            // None of fields on `CPlayerPedTargeting` and `CWanted` are accessed with direct offsets from
            // `CPlayerInfo` instances in the game code

            // The offset changed from 0x2f0 in Legacy to 0x2e0 in Enhanced
            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 81 c1 ? ? ? ? 49 8b 94 24");
                if (address != null)
                {
                    CPlayerPedTargetingOfffset = *(int*)(address + 3);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x85\xFF\x74\x23\x48\x85\xDB\x74\x26", "xxxxxxxxxx");
                if (address != null)
                {
                    CPlayerPedTargetingOfffset = *(int*)(address - 7);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 0f 44 c8 f3 0f 10 05");
                if (address != null)
                {
                    CWantedOffset = *(int*)(address - 7);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x85\xFF\x74\x3F\x48\x8B\xCF\xE8", "xxxxxxxxx");
                if (address != null)
                {
                    CWantedOffset = *(int*)(address + 0x1B);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("8b 86 ? ? ? ? 89 46 ? 83 be");
                if (address != null)
                {
                    CurrentCrimeValueOffset = (int)*(byte*)(address + 8); // 0x20
                    TimeWhenNewCrimeValueTakesEffectOffset = (int)*(byte*)(address - 13); // 0x2c
                    CurrentWantedLevelOffset = *(int*)(address + 11); // 0xb8
                    NewCrimeValueOffset = *(int*)(address + 2); // 0xc0
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x45\x84\xC9\x74\x32\x8B\x41\x00\x85\xC0\x74\x2B", "xxxxxxx?xxxx");
                if (address != null)
                {
                    CurrentCrimeValueOffset = (int)*(byte*)(address + 0x2A);
                    TimeWhenNewCrimeValueTakesEffectOffset = (int)*(byte*)(address + 0x7);
                    CurrentWantedLevelOffset = *(int*)(address + 0x17);
                    NewCrimeValueOffset = *(int*)(address + 0x1D);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("0f b6 86 ? ? ? ? a8 ? 75 ? 48 85 ed");
            }
            else
            {
                address = MemScanner.FindPatternBmh("\xF6\x87\x00\x00\x00\x00\x02\x44\x8B\x00\x00\x00\x00\x00\x75\x0E", "xx??xxxxx?????xx");
            }
            if (address != null)
            {
                int isWantedStarFlashingOffset = *(int*)(address + (s_isEnhanced? 3 : 2));
                // Flags for ignoring player are actually read/written as a byte in the game code, but make this value 4-byte aligned because SetBit and IsBitSet reads/writes as an int value

                // TODO: Test these for Enhanced.
                CWantedIgnorePlayerFlagOffset = isWantedStarFlashingOffset - 3;

                CWantedTimeSearchLastRefocusedOffset = isWantedStarFlashingOffset - 0x23;
                CWantedTimeLastSpottedOffset = CWantedTimeSearchLastRefocusedOffset + 0x4;
                CWantedTimeHiddenEvasionStartedOffset = CWantedTimeSearchLastRefocusedOffset + 0xC;
            }

            if (s_isEnhanced)
            {
                // getting the address of the function from a call site and avoiding crossing label boundaries by using a 2-step scan.
                startAddressToSearch = new IntPtr(MemScanner.FindPatternBmh("0f 29 74 24 ? 48 85 c9 0f 84 ? ? ? ? 48 89 ce e8"));
                if (startAddressToSearch != IntPtr.Zero)
                {
                    address = MemScanner.FindPatternBmh("4c 89 e1 e8 ? ? ? ? e9", startAddressToSearch);
                    if (address != null)
                    {
                        s_activateSpecialAbilityFunc = (delegate* unmanaged[Stdcall]<IntPtr, void>)(new IntPtr(*(int*)(address + 4) + address + 8));
                    }
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\xEB\x26\x8B\x87\x00\x00\x00\x00\x25\x00\xF8\xFF\xFF\xC1\xE0\x12", "xxxx????xxxxxxxx");
                if (address != null)
                {
                    s_activateSpecialAbilityFunc = (delegate* unmanaged[Stdcall]<IntPtr, void>)(new IntPtr(*(int*)(address + 0x24) + address + 0x28));
                }
            }

            int gameVersion = GetGameVersion();
            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 89 f1 e8 ? ? ? ? 48 85 c0 0f 84 ? ? ? ? 49 89 c4");
                if (address != null)
                {
                    s_getSpecialAbilityAddressFunc = (delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr>)(new IntPtr(*(int*)(address + 4) + address + 8));
                }
            }
            else
            {   
                // Two special ability slots are available in b2060 and later versions
                if (gameVersion >= 59)
                {
                    address = MemScanner.FindPatternBmh("\x0F\x84\x49\x01\x00\x00\x33\xD2\xE8", "xxxxxxxxx");
                    if (address != null)
                    {
                        s_getSpecialAbilityAddressFunc = (delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr>)(new IntPtr(*(int*)(address + 9) + address + 13));
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x0F\x84\x46\x01\x00\x00\x48\x8B\x9B", "xxxxxxxxx");
                    if (address != null)
                    {
                        PlayerPedSpecialAbilityOffset = *(int*)(address + 9);
                    }
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("4c 8b b0 ? ? ? ? 41 0f b7 b5");
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x8B\x87\x00\x00\x00\x00\x48\x85\xC0\x0F\x84\x8B\x00\x00\x00", "xxx????xxxxxxxxx");
            }
            if (address != null)
            {
                s_objParentEntityAddressDetachedFromOffset = *(int*)(address + 3);
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("89 d5 89 ce 48 8d 3d");
                if (address != null)
                {
                    s_projectilePoolAddress = (ulong*)(*(int*)(address + 7) + address + 11);
                    s_projectileCountAddress = (int*)(*(int*)(address - 7) + address - 2);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x8D\x1D\x00\x00\x00\x00\x4C\x8B\x0B\x4D\x85\xC9\x74\x67", "xxx????xxxxxxxx");
                if (address != null)
                {
                    s_projectilePoolAddress = (ulong*)(*(int*)(address + 16) + address + 20);
                    // Find address of the projectile count, just in case the max number of projectile changes from 50
                    s_projectileCountAddress = (int*)(*(int*)(address - 4) + address);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 39 88 ? ? ? ? 75 ? 85 d2");
                if (address != null)
                {
                    ProjectileOwnerOffset = *(int*)(address + 3);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x48\x85\xED\x74\x09\x48\x39\xA9\x00\x00\x00\x00\x75\x2D", "xxxxxxxx????xx");
                if (address != null)
                {
                    ProjectileOwnerOffset = *(int*)(address + 8);
                }
            }

            if (s_isEnhanced)
            {
                // could've been bundled with the scan for ProjectileOwnerOffset, but created another pattern for consistency.
                address = MemScanner.FindPatternBmh("4c 8b 98 ? ? ? ? 41 39 53 ? 74");
                if (address != null)
                {
                    ProjectileAmmoInfoOffset = *(int*)(address + 3);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x45\x85\xF6\x74\x0D\x48\x8B\x81\x00\x00\x00\x00\x44\x39\x70\x10", "xxxxxxxx????xxxx");
                if (address != null)
                {
                    ProjectileAmmoInfoOffset = *(int*)(address + 8);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("74 ? 48 8b 88 ? ? ? ? 49 3b 0e");
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x0F\x84\xBE\x00\x00\x00\x48\x8B\x0E\x48\x39\x88\x00\x00\x00\x00\x0F\x85\xAE\x00\x00\x00", "xxxxxxxxxxxx??xxxxxxxx");

            }
            if (address != null)
            {
                s_getAsCProjectileRocketConstVFuncOffset = *(int*)(address - 7);
                // Test these for Enhanced.
                s_getAsCProjectileConstVFuncOffset = s_getAsCProjectileRocketConstVFuncOffset - 0x10;
                s_getAsCProjectileThrownConstVFuncOffset = s_getAsCProjectileRocketConstVFuncOffset + 0x10;
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("74 ? 48 39 b8 ? ? ? ? 75 ? 4c 89 f1");

            }
            else
            {
                address = MemScanner.FindPatternBmh("\x74\x33\x48\x39\x98\x00\x00\x00\x00\x75\x2A\x48\x8B\x0F\x48\x3B\xCB", "xxxxx??xxxxxxxxxx");
            }
            if (address != null)
            {
                ProjectileRocketTargetOffset = *(int*)(address + 5);
                // Test these for Enhanced.
                ProjectileRocketCachedTargetPosOffset = ProjectileRocketTargetOffset - 0x20;
                ProjectileRocketLaunchDirOffset = ProjectileRocketTargetOffset - 0x10;
                ProjectileRocketFlightModelInputPitchOffset = ProjectileRocketTargetOffset + 0x8;
                ProjectileRocketFlightModelInputRollOffset = ProjectileRocketTargetOffset + 0xC;
                ProjectileRocketFlightModelInputYawOffset = ProjectileRocketTargetOffset + 0x10;

                ProjectileRocketTimeBeforeHomingOffset = ProjectileRocketTargetOffset + 0x18;
                ProjectileRocketTimeBeforeHomingAngleBreakOffset = ProjectileRocketTargetOffset + 0x1C;
                ProjectileRocketLauncherSpeedOffset = ProjectileRocketTargetOffset + 0x20;
                ProjectileRocketTimeSinceLaunchOffset = ProjectileRocketTargetOffset + 0x24;
                ProjectileRocketFlagsOffset = ProjectileRocketTargetOffset + 0x30;
                ProjectileRocketCachedDirectionOffset = ProjectileRocketTargetOffset + 0x40;
            }

            // EDX was changed to ECX in enhanced.
            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("40 84 ed 74 ? 31 d2 e8");
            } else
            {
                address = MemScanner.FindPatternBmh("40 84 ed 74 ? 33 d2 e8 ? ? ? ? eb");
            }
            if (address != null)
            {
                s_explodeProjectileFunc = (delegate* unmanaged[Stdcall]<IntPtr, int, void>)(new IntPtr(*(int*)(address + 8) + address + 12));
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("77 ? 48 8b 03 48 89 d9 ff 50 ? 48 85 c0");
                if (address != null)
                {
                    s_getFragInstVFuncOffset = *(sbyte*)(address + 10);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x0F\x84\x8F\x00\x00\x00\x8A\x48\x28\x80\xE9\x02\x80\xF9\x03\x0F\x87\x80\x00\x00\x00\x48\x8B\x10\x48\x8B\xC8\xFF\x52", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
                if (address != null)
                {
                    // The offset is 0x78 in b2944, and one more v func addition before this func makes us have to create another memory pattern/signature
                    s_getFragInstVFuncOffset = *(sbyte*)(address + 0x1D);
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("e8 ? ? ? ? 4c 89 f1 48 89 f2 41 89 d8");
                if (address != null)
                {
                    s_detachFragmentPartByIndexFunc = (delegate* unmanaged[Stdcall]<FragInst*, int, FragInst*>)(new IntPtr(*(int*)(address + 1) + address + 5));
                }
            }
            else
            {
                address = MemScanner.FindPatternNaive("\x0F\xBE\x5E\x06\x48\x8B\xCF\xFF\x50\x00\x8B\xD3\x48\x8B\xC8\xE8\x00\x00\x00\x00\x8B\x4E", "xxxxxxxxx?xxxxxx????xx");
                if (address != null)
                {
                    s_detachFragmentPartByIndexFunc = (delegate* unmanaged[Stdcall]<FragInst*, int, FragInst*>)(new IntPtr(*(int*)(address + 16) + address + 20));
                }
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("66 83 7a ? ? 0f 85 ? ? ? ? 48 8b 0d");
                if (address != null)
                {
                    s_phSimulatorInstPtr = (ulong**)(*(int*)(address + 14) + address + 18);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x74\x56\x48\x8B\x0D\x00\x00\x00\x00\x41\x0F\xB7\xD0\x45\x33\xC9\x45\x33\xC0", "xxxxx????xxxxxxxxxx");
                if (address != null)
                {
                    s_phSimulatorInstPtr = (ulong**)(*(int*)(address + 5) + address + 9);
                }
            }

            // Same pattern for Enhanced and Legacy.
            address = MemScanner.FindPatternBmh("48 63 87 ? ? ? ? 3b 87");
            if (address != null)
            {
                s_colliderCapacityOffset = *(int*)(address + 9); // 0x860
                s_colliderCountOffset = s_colliderCapacityOffset + 4;
            }

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("48 89 c6 48 8b 05 ? ? ? ? 48 85 c0 74 ? 48 8b 78");
                if (address != null)
                {
                    InteriorProxyPtrFromGameplayCamAddress = (ulong*)(*(int*)(address + 6) + address + 10);
                    InteriorInstPtrInInteriorProxyOffset = (int)*(byte*)(address + 18); // 0x30
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x7E\x63\x48\x89\x5C\x24\x08\x57\x48\x83\xEC\x20", "xxxxxxxxxxxx");
                if (address != null)
                {
                    InteriorProxyPtrFromGameplayCamAddress = (ulong*)(*(int*)(address + 37) + address + 41);
                    InteriorInstPtrInInteriorProxyOffset = (int)*(byte*)(address + 49); // 0x48
                }
            }

            // TODO: create a central patch manager which stores original and patch bytes.

            // These 2 patches are done by trainers such as Simple Trainer, Menyoo, and Enhanced Native Trainer, but we try to do this if they are not done yet
            #region -- Bypass model requests block for some models --

            // This enables to spawn some drawable objects without a dedicated collision (e.g. prop_fan_palm_01a).

            if (s_isEnhanced)
            {
                address = MemScanner.FindPatternBmh("44 0f b6 b4 24 ? ? ? ? 41 20 f6");
                if (address != null)
                {
                    address = address - 20;
                    // Here we add a jmp 0x12 and nop the rest.
                    jmpPatchHelper(address, 0x12);
                }
            }
            else
            {
                address = MemScanner.FindPatternBmh("\x40\x84\x00\x74\x13\xE8\x00\x00\x00\x00\x48\x85\xC0\x75\x09\x38\x45\x57\x0F\x84", "xx?xxx????xxxxxxxxxx");
                if (address != null)
                {
                    // Find address to patch because some of the instructions are changed and offset differs between b1290 and b1180
                    // Skip the region where there are no "lea rcx, [rbp+6F]"
                    address = MemScanner.FindPatternNaive("\x33\xC1\x48\x8D\x4D\x6F", "xxxxxx", new IntPtr(address + 0x3A), 0x30);
                    address = address != null ? (address + 0x16) : null;
                    if (address != null && *address != 0x90) // Note: This check is not exhaustive, since this could be patched using a jmp (like we do above and below).
                    {
                        const int bytesToWriteInstructions = 0x18;
                        byte[] nopBytes = Enumerable.Repeat((byte)0x90, bytesToWriteInstructions).ToArray();
                        Marshal.Copy(nopBytes, 0, new IntPtr(address), bytesToWriteInstructions);
                    }
                }
                else
                {
                    // The first pattern is not present in the current versions of Legacy. I am however unsure when it stopped working.
                    // For that reason, we look for another pattern when the first scan fails in newer versions of legacy.
                    address = MemScanner.FindPatternBmh("45 84 e4 74 ? e8 ? ? ? ? 48 85 c0");
                    if (address != null)
                    {
                        address = address - 24;
                        // Here we add a jmp 0x16 and nop the rest.
                        jmpPatchHelper(address, 0x16);
                    }
                }
            }

            #endregion
            #region -- Bypass is player model allowed to spawn checks --
            address = s_isEnhanced ? MemScanner.FindPatternBmh("74 ? e8 ? ? ? ? 48 8b b0") : MemScanner.FindPatternBmh("40 53 48 83 ec ? e8 ? ? ? ? 48 8b d8 48 85 c0 74 ? 48 8b 10 48 8b c8 ff 52");
            address = address != null ? (address + 28) : null;
            if (address != null)
            {
                int bytesToWriteInstructions = s_isEnhanced ? 6 : 4;
                byte[] nopBytes = Enumerable.Repeat((byte)0x90, bytesToWriteInstructions).ToArray();
                Marshal.Copy(nopBytes, 0, new IntPtr(address), bytesToWriteInstructions);
            }
            #endregion

            // Generate vehicle model list
            var vehicleHashesGroupedByClass = new List<int>[0x20];
            for (int i = 0; i < 0x20; i++)
            {
                vehicleHashesGroupedByClass[i] = new List<int>();
            }

            var vehicleHashesGroupedByType = new List<int>[0x10];
            for (int i = 0; i < 0x10; i++)
            {
                vehicleHashesGroupedByType[i] = new List<int>();
            }

            var weaponObjectHashes = new List<int>();
            var pedHashes = new List<int>();

            
            HashSet<uint> excludePeds;
            excludePeds = new HashSet<uint>
            {
                // Creating a Ped with this model returns an invalid handle.
                1173958009u, /* MP_HeadTargets */
                // The game crashes if attempting to create a Ped with these models.
                1057201338u, /* slod_human */
                2238511874u, /* slod_large_quadped */
                762327283u,  /* slod_small_quadped */
            };

            HashSet<uint> stubVehicles;
            if (s_isEnhanced)
            { 
                stubVehicles = new HashSet<uint>();
            }
            else
            {
                // The game (Legacy) will crash when it load these vehicles because of the stub vehicle models
                stubVehicles = new HashSet<uint> {
                0xA71D0D4F, /* astron2 */
                0x170341C2, /* cyclone2 */
                0x5C54030C, /* arbitergt */
                0x39085F47, /* ignus2 */
                0x438F6593, /* s95 */
                };
            }
 

            if (vehicleClassOffset != 0)
            {
                for (int i = 0; i < s_modelHashEntries; i++)
                {
                    for (HashNode* cur = ((HashNode**)s_modelHashTable)[i]; cur != null; cur = cur->next)
                    {
                        ushort data = cur->data;
                        bool bitTest = ((*(int*)(s_modelNum2 + (ulong)(4 * data >> 5))) & (1 << (data & 0x1F))) != 0;
                        if (data >= s_modelNum1 || !bitTest)
                        {
                            continue;
                        }

                        ulong addr1 = s_modelNum4 + s_modelNum3 * data;
                        if (addr1 == 0)
                        {
                            continue;
                        }

                        ulong addr2 = *(ulong*)(addr1);
                        if (addr2 != 0)
                        {
                            switch ((ModelInfoClassType)(*(byte*)(addr2 + 157) & 0x1F)) // TODO: find the offsets dynamically
                            {
                                case ModelInfoClassType.Weapon:
                                    weaponObjectHashes.Add(cur->hash);
                                    break;
                                case ModelInfoClassType.Vehicle:
                                    // Avoid loading stub vehicles since it will crash the game
                                    if (stubVehicles.Contains((uint)cur->hash))
                                    {
                                        continue;
                                    }

                                    vehicleHashesGroupedByClass[*(byte*)(addr2 + vehicleClassOffset) & 0x1F].Add(cur->hash);

                                    // Normalize the value to vehicle type range for b944 or later versions if current game version is earlier than b944.
                                    // The values for CAmphibiousAutomobile and CAmphibiousQuadBike were inserted between those for CSubmarineCar and CHeli in b944.
                                    int vehicleTypeInt = *(int*)((byte*)addr2 + s_vehicleTypeOffsetInModelInfo);
                                    if (!s_isEnhanced && gameVersion < 28 && vehicleTypeInt >= 6)
                                    {
                                        vehicleTypeInt += 2;
                                    }

                                    vehicleHashesGroupedByType[vehicleTypeInt].Add(cur->hash);

                                    break;
                                case ModelInfoClassType.Ped:
                                    if (excludePeds.Contains((uint)cur->hash)) {
                                        continue;
                                    }
                                    pedHashes.Add(cur->hash);
                                    break;
                            }
                        }
                    }
                }
            }

            var vehicleResult = new ReadOnlyCollection<int>[0x20];
            for (int i = 0; i < 0x20; i++)
            {
                vehicleResult[i] = Array.AsReadOnly(vehicleHashesGroupedByClass[i].ToArray());
            }

            VehicleModels = Array.AsReadOnly(vehicleResult);

            vehicleResult = new ReadOnlyCollection<int>[0x10];
            for (int i = 0; i < 0x10; i++)
            {
                vehicleResult[i] = Array.AsReadOnly(vehicleHashesGroupedByType[i].ToArray());
            }

            VehicleModelsGroupedByType = Array.AsReadOnly(vehicleResult);

            WeaponModels = Array.AsReadOnly(weaponObjectHashes.ToArray());
            PedModels = Array.AsReadOnly(pedHashes.ToArray());

            #region -- Enable All DLC Vehicles --

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("48 03 05 ? ? ? ? 4c 85 c0 0f 84 ? ? ? ? e9");
                }
                else
                {
                    // no need to patch the global variable in v1.0.573.1 or older builds
                    if (gameVersion <= 15)
                    {
                        return;
                    }
                    address = MemScanner.FindPatternBmh("\x48\x03\x15\x00\x00\x00\x00\x4C\x23\xC2\x49\x8B\x08", "xxx????xxxxxx");
                }

                if (address == null)
                {
                    return;
                }

                var yscScriptTable = (YscScriptTable*)(*(int*)(address + 3) + address + 7);

                // find the shop_controller script
                YscScriptTableItem* shopControllerItem = yscScriptTable->FindScript(0x39DA738B);

                if (shopControllerItem == null || !shopControllerItem->IsLoaded())
                {
                    return;
                }

                YscScriptHeader* shopControllerHeader = shopControllerItem->header;

                string enableCarsGlobalPattern;
                string enableCarsGlobalMask;
                int enableCarsGlobalOffset;
                if (s_isEnhanced)
                {
                    // Given that we look for this pattern in each codePage of the shop_controller script,
                    // this pattern will be logged as not found multiple times in debug shvdne builds. Simply ignore that.
                    enableCarsGlobalPattern = "\x2D\x00\x00\x00\x00\x2C\x00\x00\x00\x56\x00\x00\x71\x2E\x00\x00\x62";
                    enableCarsGlobalMask = "x????x???x??xx??x";
                    enableCarsGlobalOffset = 17;
                }
                else
                {
                    if (gameVersion >= 80)
                    {
                        // b2802 has 3 additional opcodes between CALL opcode (0x5D) and GLOBAL_U24 opcode (0x61 in b2802)
                        enableCarsGlobalPattern = "\x2D\x00\x00\x00\x00\x2C\x01\x00\x00\x56\x04\x00\x71\x2E\x00\x01\x62\x00\x00\x00\x00\x04\x00\x71\x2E\x00\x01";
                    }
                    else if (gameVersion >= 46)
                    {
                        enableCarsGlobalPattern = "\x2D\x00\x00\x00\x00\x2C\x01\x00\x00\x56\x04\x00\x6E\x2E\x00\x01\x5F\x00\x00\x00\x00\x04\x00\x6E\x2E\x00\x01";
                    }
                    else
                    {
                        enableCarsGlobalPattern = "\x2D\x00\x00\x00\x00\x2C\x01\x00\x00\x56\x04\x00\x6E\x2E\x00\x01\x5F\x00\x00\x00\x00\x04\x00\x6E\x2E\x00\x01";
                    }
                    enableCarsGlobalMask = gameVersion >= 46 ? "x??xxxx??xxxxx?xx????xxxx?x" : "xx??xxxxxx?xx????xxxx?x";
                    enableCarsGlobalOffset = gameVersion >= 46 ? 17 : 13;
                }
                int codepageCount = shopControllerHeader->CodePageCount();
                for (int i = 0; i < codepageCount; i++)
                {
                    int size = shopControllerHeader->GetCodePageSize(i);
                    if (size <= 0)
                    {
                        continue;
                    }

                    address = MemScanner.FindPatternNaive(enableCarsGlobalPattern, enableCarsGlobalMask, shopControllerHeader->GetCodePageAddress(i), (ulong)size);
                    if (address == null)
                    {
                        continue;
                    }

                    int globalIndex = *(int*)(address + enableCarsGlobalOffset) & 0xFFFFFF; // TODO: expose a getter and a setter for this global.
                    *(int*)GetGlobalPtr(globalIndex).ToPointer() = 1;
                    break;
                }
            
            #endregion
        }

        public static bool s_isEnhanced { get; private set;}

        public static IntPtr String { get; private set; } // "~a~"
        public static IntPtr NullString { get; private set; } // ""
        public static IntPtr CellEmailBcon { get; private set; } // "~a~~a~~a~~a~~a~~a~~a~~a~~a~~a~"

        // Script Hook V's implementation uses `GetModuleHandleA` and searches the exe image for "FileVersion" info,
        // and this can be substituted with dotnet's standard library. We don't want to rely on 's new API unless
        // absolutely necessary.
        // Also, SHV's implementation does not use a mutex lock while variables for version cache can be read and
        // written in multiple threads, which can lead potential issues due to race condition (at least as of
        // the version 28 Sep 2024). SHV's `getGameVersionInfo` writes the retrieved value to the cache variable
        // the first time it is called, and this can only happen after some ASI script is loaded unless some
        // ASI plugin that doesn't rely on SHV bothers invoking `getGameVersionInfo`.
        //
        // Getting file version info from the process image is too expensive to execute every time we need to
        // determine memory offsets to read, since it could take 1 ms while reading a cached value would only take
        // 500 ns in the same environment even if a reader write lock is used (but we don't need to use one in our
        // case). Since `NativeMemory` won't be visible before the constructor is performed and all of
        // the underlying fields of `System.Version` are read-only, we can read the instance without using a mutex
        // lock.
        public static Version GameFileVersion { get; }

        private static float DistanceToSquared(float x1, float y1, float z1, float x2, float y2, float z2)
        {
            float deltaX = x1 - x2;
            float deltaY = y1 - y2;
            float deltaZ = z1 - z2;

            return deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;
        }

        #region -- fwRefAwareBaseImpl Functions --

        private static delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void> s_fwRefAwareBaseImpl__AddKnownRef;
        private static delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void> s_fwRefAwareBaseImpl__RemoveKnownRef;
        private static delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void> s_fwRefAwareBaseImpl__RemoveThenAddKnownRef;

        #endregion

        #region -- fwRegdRef Functions --

        internal sealed class ResetFwRegdRefTask : IScriptTask
        {
            #region Fields
            private IntPtr _lhs;
            private IntPtr _rhs;
            #endregion

            internal ResetFwRegdRefTask(IntPtr lhs, IntPtr rhs)
            {
                _lhs = lhs;
                _rhs = rhs;
            }

            public void Run()
            {
                AssignToFwRegdRefInternal(_lhs, _rhs);
            }
        }

        /// <summary>
        /// Assigns a `<c>fwRegdRef</c>`.
        /// </summary>
        /// <param name="lhs">
        /// The <c>fwRegdRef</c> address to put the copy to. Must be one of the subclasses of
        /// `<c>fwRefAwareBaseImpl</c>`.
        /// </param>
        /// <param name="rhs">The other <c>fwRegdRef</c> to copy reference.</param>
        public static void AssignToFwRegdRef(IntPtr lhs, IntPtr rhs)
        {
            var task = new ResetFwRegdRefTask(lhs, rhs);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);
        }

        /// <summary>
        /// Assigns a `<c>fwRegdRef</c>`.
        /// </summary>
        /// <param name="lhs">
        /// The <c>fwRegdRef</c> address to put the copy to. Must be one of the subclasses of
        /// `<c>fwRefAwareBaseImpl</c>`.
        /// </param>
        /// <param name="rhs">The other <c>fwRegdRef</c> to copy reference.</param>
        private static void AssignToFwRegdRefInternal(IntPtr lhs, IntPtr rhs)
        {
            if (lhs == IntPtr.Zero)
            {
                return;
            }

            if (s_isEnhanced)
            {
                s_fwRefAwareBaseImpl__RemoveThenAddKnownRef(lhs, rhs);
                return;
            }

            IntPtr oldFwRegdRef = (IntPtr)(*(long*)(lhs));

            if (oldFwRegdRef == rhs)
            {
                return;
            }

            if (oldFwRegdRef != IntPtr.Zero)
            {
                s_fwRefAwareBaseImpl__RemoveKnownRef(oldFwRegdRef, lhs);
            }

            *(ulong*)lhs = (ulong)rhs;

            IntPtr newFwRegdRef = (IntPtr)(*(long*)(lhs));

            if (newFwRegdRef != IntPtr.Zero)
            {
                s_fwRefAwareBaseImpl__AddKnownRef(newFwRegdRef, lhs);
            }
        }

        #endregion

        #region -- fwExtensibleBase RTTI Sytem --

        /// <summary>
        /// Calls rage::fwExtensibleBase::GetClassId on a GTA class that is a subclass of rage::fwExtensibleBase.
        /// </summary>
        public static uint GetRageClassId(IntPtr addr)
        {
            ulong* vTable = *(ulong**)addr;

            // In the b2802 or a later exe, the function returns a hash value (not a pointer value)
            if (s_isEnhanced || GetGameVersion() >= 80)
            {
                // The function is for the game version b2802 or later ones.
                // This one directly returns a hash value (not a pointer value) unlike the previous function.
                var getClassNameHashFunc = (delegate* unmanaged[Stdcall]<uint>)(vTable[2]);
                return getClassNameHashFunc();
            }

            // The function is for game versions prior to b2802.
            // The function uses rax and rdx registers in newer versions prior to b2802 (probably since b2189), and it uses only rax register in older versions.
            // The function returns the address where the class name hash is in all versions prior to (the address will be the outVal address in newer versions).
            var getClassNameAddressHashFunc = (delegate* unmanaged[Stdcall]<ulong, uint*, uint*>)(vTable[2]);

            uint outVal = 0;
            uint* returnValueAddress = getClassNameAddressHashFunc(0, &outVal);
            return *returnValueAddress;
        }

        #endregion

        #region -- Cameras --

        private static ulong* s_cameraPoolAddress;
        private static ulong* s_gameplayCameraAddress;

        public static IntPtr GetCameraAddress(int handle)
        {
            // TODO: refactor into FwBasePool, since that's what this is.
            uint index = (uint)(handle >> 8);
            if (s_isEnhanced)
            {
                ulong poolAddr = (ulong)s_cameraPoolAddress;
                if (*(byte*)(index + *(long*)(poolAddr + 0x10)) == (byte)(handle & 0xFF))
                {
                    return new IntPtr(*(long*)(poolAddr + 0x08) + (index * *(uint*)(poolAddr + 0x1C)));
                }
                return IntPtr.Zero;
            }
            else
            {
                ulong poolAddr = *s_cameraPoolAddress;
                if (*(byte*)(index + *(long*)(poolAddr + 8)) == (byte)(handle & 0xFF))
                {
                    return new IntPtr(*(long*)poolAddr + (index * *(uint*)(poolAddr + 20)));
                }
                return IntPtr.Zero;
            }
        }
        public static IntPtr GetGameplayCameraAddress()
        {
            return new IntPtr((long)*s_gameplayCameraAddress);
        }

        #endregion

        #region -- Game Data --

        private static ulong s_getLabelTextByHashAddress;
        private static delegate* unmanaged[Stdcall]<ulong, int, ulong> s_getLabelTextByHashFunc;

        public static string GetGxtEntryByHash(int entryLabelHash)
        {
            char* entryText = (char*)s_getLabelTextByHashFunc(s_getLabelTextByHashAddress, entryLabelHash);
            return entryText != null ? StringMarshal.PtrToStringUtf8(new IntPtr(entryText)) : string.Empty;
        }

        #endregion

        #region -- YSC Script Data --

        [StructLayout(LayoutKind.Explicit)]
        private struct YscScriptHeader
        {
            [FieldOffset(0x10)]
            internal byte** codeBlocksOffset;
            [FieldOffset(0x1C)]
            internal int codeLength;
            [FieldOffset(0x24)]
            internal int localCount;
            [FieldOffset(0x2C)]
            internal int nativeCount;
            [FieldOffset(0x30)]
            internal long* localOffset;
            [FieldOffset(0x40)]
            internal long* nativeOffset;
            [FieldOffset(0x58)]
            internal int nameHash;

            internal int CodePageCount()
            {
                return (codeLength + 0x3FFF) >> 14;
            }
            internal int GetCodePageSize(int page)
            {
                return (page < 0 || page >= CodePageCount() ? 0 : (page == CodePageCount() - 1) ? codeLength & 0x3FFF : 0x4000);
            }
            internal IntPtr GetCodePageAddress(int page)
            {
                return new IntPtr(codeBlocksOffset[page]);
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct YscScriptTableItem
        {
            [FieldOffset(0x0)]
            internal YscScriptHeader* header;
            [FieldOffset(0xC)]
            internal int hash;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal bool IsLoaded()
            {
                return header != null;
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct YscScriptTable
        {
            [FieldOffset(0x0)]
            internal YscScriptTableItem* TablePtr;
            [FieldOffset(0x18)]
            internal uint count;

            internal YscScriptTableItem* FindScript(int hash)
            {
                if (TablePtr == null)
                {
                    return null; //table initialisation hasn't happened yet
                }
                for (int i = 0; i < count; i++)
                {
                    if (TablePtr[i].hash == hash)
                    {
                        return &TablePtr[i];
                    }
                }
                return null;
            }
        }


        #endregion

        #region -- Decorator Data --

        private static byte* s_isDecoratorLocked;

        public static bool IsDecoratorLocked
        {
            get => *s_isDecoratorLocked != 0;
            set => *s_isDecoratorLocked = (byte)(value ? 1 : 0);
        }

        #endregion

        #region -- World Data --

        private static int* s_cursorSpriteAddr;

        public static int CursorSprite => *s_cursorSpriteAddr;

        private static float* s_timeScaleAddress;

        public static float TimeScale => *s_timeScaleAddress;

        private static int* s_millisecondsPerGameMinuteAddress;

        public static int MillisecondsPerGameMinute
        {
            set => *s_millisecondsPerGameMinuteAddress = value;
        }

        private static byte* s_isClockPausedAddress;

        public static bool IsClockPaused => *s_isClockPausedAddress != 0;

        private static int* s_lastClockTickAddress;

        public static int LastTimeClockTicked
        {
            get => *s_lastClockTickAddress;
            set => *s_lastClockTickAddress = value;
        }

        private static float* s_readWorldGravityAddress;
        private static float* s_writeWorldGravityAddress;

        public static float WorldGravity
        {
            get => *s_readWorldGravityAddress;
            set => *s_writeWorldGravityAddress = value;
        }

        #endregion

        #region -- Skeleton Data --

        private static CrSkeleton* GetCrSkeletonFromEntityHandle(int handle)
        {
            IntPtr entityAddress = GetEntityAddress(handle);
            if (entityAddress == IntPtr.Zero)
            {
                return null;
            }

            return GetCrSkeletonOfEntity(entityAddress);
        }

        private static CrSkeleton* GetCrSkeletonOfEntity(IntPtr entityAddress)
        {
            FragInst* fragInst = GetFragInstAddressOfEntity(entityAddress);
            // Return value will not be null if the entity is a CVehicle or a CPed
            if (fragInst != null)
            {
                return GetEntityCrSkeletonOfFragInst(fragInst);
            }

            ulong unkAddr = *(ulong*)(entityAddress + 80);
            if (unkAddr == 0)
            {
                return null;
            }

            return (CrSkeleton*)*(ulong*)(unkAddr + 40);
        }

        private static CrSkeleton* GetEntityCrSkeletonOfFragInst(FragInst* fragInst)
        {
            FragCacheEntry* fragCacheEntry = fragInst->fragCacheEntry;
            GtaFragType* gtaFragType = fragInst->gtaFragType;

            // Check if either pointer is null just like native functions that take a bone index argument
            if (fragCacheEntry == null || gtaFragType == null)
            {
                return null;
            }

            return fragCacheEntry->crSkeleton;
        }

        public static int GetBoneIdForEntityBoneIndex(int entityHandle, int boneIndex)
        {
            if (boneIndex < 0)
            {
                return -1;
            }

            CrSkeleton* crSkeleton = GetCrSkeletonFromEntityHandle(entityHandle);
            if (crSkeleton == null)
            {
                return -1;
            }

            return crSkeleton->skeletonData->GetBoneIdByIndex(boneIndex);
        }
        public static void GetNextSiblingBoneIndexAndIdOfEntityBoneIndex(int entityHandle, int boneIndex, out int nextSiblingBoneIndex, out int nextSiblingBoneTag)
        {
            if (boneIndex < 0)
            {
                nextSiblingBoneIndex = -1;
                nextSiblingBoneTag = -1;
                return;
            }

            CrSkeleton* crSkeleton = GetCrSkeletonFromEntityHandle(entityHandle);
            if (crSkeleton == null)
            {
                nextSiblingBoneIndex = -1;
                nextSiblingBoneTag = -1;
                return;
            }

            crSkeleton->skeletonData->GetNextSiblingBoneIndexAndId(boneIndex, out nextSiblingBoneIndex, out nextSiblingBoneTag);
        }
        public static void GetParentBoneIndexAndIdOfEntityBoneIndex(int entityHandle, int boneIndex, out int parentBoneIndex, out int parentBoneTag)
        {
            if (boneIndex < 0)
            {
                parentBoneIndex = -1;
                parentBoneTag = -1;
                return;
            }

            CrSkeleton* crSkeleton = GetCrSkeletonFromEntityHandle(entityHandle);
            if (crSkeleton == null)
            {
                parentBoneIndex = -1;
                parentBoneTag = -1;
                return;
            }

            crSkeleton->skeletonData->GetParentBoneIndexAndId(boneIndex, out parentBoneIndex, out parentBoneTag);
        }
        public static string GetEntityBoneName(int entityHandle, int boneIndex)
        {
            if (boneIndex < 0)
            {
                return null;
            }

            CrSkeleton* crSkeleton = GetCrSkeletonFromEntityHandle(entityHandle);
            if (crSkeleton == null)
            {
                return null;
            }

            return crSkeleton->skeletonData->GetBoneName(boneIndex);
        }
        public static int GetEntityBoneCount(int handle)
        {
            CrSkeleton* crSkeleton = GetCrSkeletonFromEntityHandle(handle);
            return crSkeleton != null ? crSkeleton->boneCount : 0;
        }
        public static IntPtr GetEntityBoneTransformMatrixAddress(int handle)
        {
            CrSkeleton* crSkeleton = GetCrSkeletonFromEntityHandle(handle);
            if (crSkeleton == null)
            {
                return IntPtr.Zero;
            }

            return crSkeleton->GetTransformMatrixAddress();
        }
        public static IntPtr GetEntityBoneObjectMatrixAddress(int handle, int boneIndex)
        {
            if ((boneIndex & 0x80000000) != 0) // boneIndex cant be negative
            {
                return IntPtr.Zero;
            }

            CrSkeleton* crSkeleton = GetCrSkeletonFromEntityHandle(handle);
            if (crSkeleton == null)
            {
                return IntPtr.Zero;
            }

            if (boneIndex < crSkeleton->boneCount)
            {
                return crSkeleton->GetBoneObjectMatrixAddress((boneIndex));
            }

            return IntPtr.Zero;
        }
        public static IntPtr GetEntityBoneGlobalMatrixAddress(int handle, int boneIndex)
        {
            if ((boneIndex & 0x80000000) != 0) // boneIndex cant be negative
            {
                return IntPtr.Zero;
            }

            CrSkeleton* crSkeleton = GetCrSkeletonFromEntityHandle(handle);
            if (crSkeleton == null)
            {
                return IntPtr.Zero;
            }

            if (boneIndex < crSkeleton->boneCount)
            {
                return crSkeleton->GetBoneGlobalMatrixAddress(boneIndex);
            }

            return IntPtr.Zero;
        }

        #endregion

        #region -- CEntity Functions --

        private static delegate* unmanaged[Stdcall]<float*, ulong, int, float*> s_getRotationFromMatrixFunc;
        private static delegate* unmanaged[Stdcall]<float*, ulong, int> s_getQuaternionFromMatrixFunc;

        public static void GetRotationFromMatrix(float* returnRotationArray, IntPtr matrixAddress, int rotationOrder = 2)
        {
            s_getRotationFromMatrixFunc(returnRotationArray, (ulong)matrixAddress.ToInt64(), rotationOrder);

            const float rad2Deg = 57.2957763671875f; // 0x42652EE0 in hex. Exactly the same value as the GET_ENTITY_ROTATION multiplies the rotation values in radian by.
            returnRotationArray[0] *= rad2Deg;
            returnRotationArray[1] *= rad2Deg;
            returnRotationArray[2] *= rad2Deg;
        }
        public static void GetQuaternionFromMatrix(float* returnRotationArray, IntPtr matrixAddress)
        {
            s_getQuaternionFromMatrixFunc(returnRotationArray, (ulong)matrixAddress.ToInt64());
        }

        #endregion

        #region -- CPhysical Offsets --

        public static int EntityMaxHealthOffset { get; }
        public static int SetAngularVelocityVFuncOfEntityOffset { get; }
        public static int GetAngularVelocityVFuncOfEntityOffset { get; }

        public static int CAttackerArrayOfEntityOffset { get; }
        public static int ElementCountOfCAttackerArrayOfEntityOffset { get; }
        public static int ElementSizeOfCAttackerArrayOfEntity { get; }

        #endregion

        #region -- CPhysical Functions --

        internal sealed class SetEntityAngularVelocityTask : IScriptTask
        {
            #region Fields

            private IntPtr _entityAddress;
            // return value will be the address of the temporary 4 float storage
            private delegate* unmanaged[Stdcall]<IntPtr, float*, void> _setAngularVelocityDelegate;
            private float _x, _y, _z;
            #endregion

            internal SetEntityAngularVelocityTask(IntPtr entityAddress, delegate* unmanaged[Stdcall]<IntPtr, float*, void> vFuncDelegate, float x, float y, float z)
            {
                this._entityAddress = entityAddress;
                this._setAngularVelocityDelegate = vFuncDelegate;
                this._x = x;
                this._y = y;
                this._z = z;
            }

            public void Run()
            {
                float* angularVelocity = stackalloc float[4];
                angularVelocity[0] = _x;
                angularVelocity[1] = _y;
                angularVelocity[2] = _z;

                _setAngularVelocityDelegate(_entityAddress, angularVelocity);
            }
        }

        public static float* GetEntityAngularVelocity(IntPtr entityAddress)
        {
            ulong vFuncAddr = *(ulong*)(*(ulong*)entityAddress.ToPointer() + (uint)GetAngularVelocityVFuncOfEntityOffset);
            var getEntityAngularVelocity = (delegate* unmanaged[Stdcall]<IntPtr, float*>)(vFuncAddr);

            return getEntityAngularVelocity(entityAddress);
        }

        public static void SetEntityAngularVelocity(IntPtr entityAddress, float x, float y, float z)
        {
            ulong vFuncAddr = *(ulong*)(*(ulong*)entityAddress.ToPointer() + (uint)SetAngularVelocityVFuncOfEntityOffset);
            var setEntityAngularVelocityDelegate = (delegate* unmanaged[Stdcall]<IntPtr, float*, void>)(vFuncAddr);

            var task = new SetEntityAngularVelocityTask(entityAddress, setEntityAngularVelocityDelegate, x, y, z);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);
        }



        #endregion

        #region -- CPhysical Data --

        // the size is at least 0x10 in all game versions
        [StructLayout(LayoutKind.Explicit, Size = 0x10)]
        private struct CAttacker
        {
            [FieldOffset(0x0)]
            internal ulong attackerEntityAddress;
            [FieldOffset(0x8)]
            internal int weaponHash;
            [FieldOffset(0xC)]
            internal int gameTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EntityDamageRecordForReturnValue
        {
            public int attackerEntityHandle;
            public int weaponHash;
            public int gameTime;

            public EntityDamageRecordForReturnValue(int attackerEntityHandle, int weaponHash, int gameTime)
            {
                this.attackerEntityHandle = attackerEntityHandle;
                this.weaponHash = weaponHash;
                this.gameTime = gameTime;
            }
        }

        public static bool IsIndexOfEntityDamageRecordValid(IntPtr entityAddress, uint index)
        {
            if (NativeMemory.CAttackerArrayOfEntityOffset == 0 ||
                NativeMemory.ElementCountOfCAttackerArrayOfEntityOffset == 0 ||
                NativeMemory.ElementSizeOfCAttackerArrayOfEntity == 0)
            {
                return false;
            }

            ulong entityCAttackerArrayAddress = *(ulong*)(entityAddress + NativeMemory.CAttackerArrayOfEntityOffset).ToPointer();

            if (entityCAttackerArrayAddress == 0)
            {
                return false;
            }

            int entryCount = *(int*)((byte*)entityCAttackerArrayAddress + NativeMemory.ElementCountOfCAttackerArrayOfEntityOffset);

            return index < entryCount;
        }

        private static EntityDamageRecordForReturnValue GetEntityDamageRecordEntryAtIndexInternal(ulong cAttackerArrayAddress, uint index)
        {
            var cAttacker = (CAttacker*)((byte*)cAttackerArrayAddress + index * NativeMemory.ElementSizeOfCAttackerArrayOfEntity);

            ulong attackerEntityAddress = cAttacker->attackerEntityAddress;
            int weaponHash = cAttacker->weaponHash;
            int gameTime = cAttacker->gameTime;
            int attackerHandle = attackerEntityAddress != 0 ? GetEntityHandleFromAddress(new IntPtr((long)attackerEntityAddress)) : 0;

            return new EntityDamageRecordForReturnValue(attackerHandle, weaponHash, gameTime);
        }
        public static EntityDamageRecordForReturnValue GetEntityDamageRecordEntryAtIndex(IntPtr entityAddress, uint index)
        {
            ulong entityCAttackerArrayAddress = *(ulong*)(entityAddress + NativeMemory.CAttackerArrayOfEntityOffset).ToPointer();

            if (entityCAttackerArrayAddress == 0)
            {
                return default(EntityDamageRecordForReturnValue);
            }

            return GetEntityDamageRecordEntryAtIndexInternal(entityCAttackerArrayAddress, index);
        }

        public static EntityDamageRecordForReturnValue[] GetEntityDamageRecordEntries(IntPtr entityAddress)
        {
            if (NativeMemory.CAttackerArrayOfEntityOffset == 0 ||
                NativeMemory.ElementCountOfCAttackerArrayOfEntityOffset == 0 ||
                NativeMemory.ElementSizeOfCAttackerArrayOfEntity == 0)
            {
                return Array.Empty<EntityDamageRecordForReturnValue>();
            }

            ulong entityCAttackerArrayAddress = *(ulong*)(entityAddress + NativeMemory.CAttackerArrayOfEntityOffset).ToPointer();

            if (entityCAttackerArrayAddress == 0)
            {
                return Array.Empty<EntityDamageRecordForReturnValue>();
            }

            int returnEntrySize = *(int*)((byte*)entityCAttackerArrayAddress + NativeMemory.ElementCountOfCAttackerArrayOfEntityOffset);
            EntityDamageRecordForReturnValue[] returnEntries = returnEntrySize != 0 ? new EntityDamageRecordForReturnValue[returnEntrySize] : Array.Empty<EntityDamageRecordForReturnValue>();

            for (uint i = 0; i < returnEntries.Length; i++)
            {
                returnEntries[i] = GetEntityDamageRecordEntryAtIndexInternal(entityCAttackerArrayAddress, i);
            }

            return returnEntries;
        }

        public static bool EntityRecordsCollision(int entityHandle)
        {
            IntPtr entityAddress = GetEntityAddress(entityHandle);
            if (entityAddress == IntPtr.Zero)
            {
                return false;
            }

            return CPhysicalRecordsCollision(entityAddress);
        }

        public static bool CPhysicalRecordsCollision(IntPtr cPhysicalAddress)
        {
            if (CAttackerArrayOfEntityOffset == 0)
            {
                return false;
            }

            int offsetToRead = CAttackerArrayOfEntityOffset + 0x47;
            return *(byte*)(cPhysicalAddress + offsetToRead) != 0;
        }

        public static bool HasEntityCollidedWithBuildingOrAnimatedBuilding(int entityHandle)
        {
            if (CAttackerArrayOfEntityOffset == 0)
            {
                return false;
            }

            int offsetToRead = CAttackerArrayOfEntityOffset + 0x8;
            return GetTargetCEntityAddressCollidingWith(entityHandle, offsetToRead) != IntPtr.Zero;
        }

        public static int GetVehicleHandleEntityIsCollidingWith(int entityHandle)
        {
            if (CAttackerArrayOfEntityOffset == 0)
            {
                return 0;
            }

            int offsetToRead = CAttackerArrayOfEntityOffset + 0x10;
            return GetPhysicalEntityHandleEntityIsCollidingWith(entityHandle, offsetToRead);
        }

        public static int GetPedHandleEntityIsCollidingWith(int entityHandle)
        {
            if (CAttackerArrayOfEntityOffset == 0)
            {
                return 0;
            }

            int offsetToRead = CAttackerArrayOfEntityOffset + 0x18;
            return GetPhysicalEntityHandleEntityIsCollidingWith(entityHandle, offsetToRead);
        }

        // maybe there's another collision record entry for CObject, but we're not sure about this
        public static int GetPropHandleEntityIsCollidingWith(int entityHandle)
        {
            if (CAttackerArrayOfEntityOffset == 0)
            {
                return 0;
            }

            int offsetToRead = CAttackerArrayOfEntityOffset + 0x20;
            return GetPhysicalEntityHandleEntityIsCollidingWith(entityHandle, offsetToRead);
        }

        public static int GetPhysicalEntityHandleFromLastCollisionEntryOfEntity(int entityHandle)
        {
            if (CAttackerArrayOfEntityOffset == 0)
            {
                return 0;
            }

            int offsetToRead = CAttackerArrayOfEntityOffset + 0x30;
            IntPtr targetCEntityAddress = GetTargetCEntityAddressCollidingWith(entityHandle, offsetToRead);

            if (targetCEntityAddress == IntPtr.Zero)
            {
                return 0;
            }

            var targetCEntityType = GetEntityTypeInternal((ulong)targetCEntityAddress.ToInt64());
            switch (targetCEntityType)
            {
                case EntityTypeInternal.Vehicle:
                case EntityTypeInternal.Ped:
                case EntityTypeInternal.Object:
                    return GetEntityHandleFromAddress(targetCEntityAddress);
                default:
                    return 0;
            }
        }

        private static int GetPhysicalEntityHandleEntityIsCollidingWith(int entityHandle, int offsetOfCollisionRecord)
        {
            IntPtr targetCEntityAddress = GetTargetCEntityAddressCollidingWith(entityHandle, offsetOfCollisionRecord);
            if (targetCEntityAddress == IntPtr.Zero)
            {
                return 0;
            }

            return GetEntityHandleFromAddress(targetCEntityAddress);
        }

        private static IntPtr GetTargetCEntityAddressCollidingWith(int entityHandle, int offsetOfCollisionRecord)
        {
            IntPtr entityAddress = GetEntityAddress(entityHandle);
            if (entityAddress == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (!CPhysicalRecordsCollision(entityAddress))
            {
                return IntPtr.Zero;
            }

            long** collisionRecord = *(long***)(entityAddress + offsetOfCollisionRecord);
            if (collisionRecord == null)
            {
                return IntPtr.Zero;
            }

            long* targetCEntityAddress = *collisionRecord;
            if (targetCEntityAddress == null)
            {
                return IntPtr.Zero;
            }

            return new IntPtr(targetCEntityAddress);
        }

        // Found at EntityAddr + 0x28 In Legacy and Enhanced.
        // TODO: Find it dynamically to be safe.
        private enum EntityTypeInternal
        {
            Invalid = 0,
            Building = 1,
            AnimatedBuilding = 2,
            Vehicle = 3,
            Ped = 4,
            Object = 5,
            ParticleEffect = 48
        }

        #endregion

        public static unsafe class Vehicle
        {
            #region -- Vehicle Offsets --

            public static int NextGearOffset { get; }
            public static int GearOffset { get; }
            public static int HighGearOffset { get; }

            public static int CurrentRpmOffset { get; }
            public static int ClutchOffset { get; }
            public static int AccelerationOffset { get; }

            public static int CVehicleEngineOffset { get; }
            public static int CVehicleEngineTurboOffset { get; }

            public static int FuelLevelOffset { get; }
            public static int OilLevelOffset { get; }

            public static int VehicleTypeOffset { get; }

            public static int WheelPtrArrayOffset { get; }
            public static int WheelCountOffset { get; }
            public static int WheelBoneIdToPtrArrayIndexOffset { get; }
            public static int WheelSpeedOffset { get; }
            public static int CanWheelBreakOffset { get; }

            public static int SteeringAngleOffset { get; }
            public static int SteeringScaleOffset { get; }
            public static int ThrottlePowerOffset { get; }
            public static int BrakePowerOffset { get; }

            public static int EngineTemperatureOffset { get; }
            public static int EnginePowerMultiplierOffset { get; }

            public static int DisablePretendOccupantOffset { get; }

            public static int ProvidesCoverOffset { get; }

            public static int LightsMultiplierOffset { get; }

            public static int IsInteriorLightOnOffset { get; }
            public static int IsEngineStartingOffset { get; }

            public static int IsWantedOffset { get; }

            public static int IsHeadlightDamagedOffset { get; }

            public static int PreviouslyOwnedByPlayerOffset { get; }
            public static int NeedsToBeHotwiredOffset { get; }

            public static int AlarmTimeOffset { get; }

            public static int LodMultiplierOffset { get; }

            public static int CanUseSirenOffset { get; }
            public static int HasMutedSirensOffset { get; }
            public static int HasMutedSirensBit { get; }

            public static int SirenBufferOffset { get; }

            public static int DropsMoneyWhenBlownUpOffset { get; }

            public static int HeliBladesSpeedOffset { get; }

            public static int HeliMainRotorHealthOffset { get; }
            public static int HeliTailRotorHealthOffset { get; }
            public static int HeliTailBoomHealthOffset { get; }

            public static int HandlingDataOffset { get; }

            public static int SubHandlingDataArrayOffset { get; }

            public static int ModelSirenIdOffset { get; }

            public static int FirstVehicleFlagsOffset { get; }

            static Vehicle()
            {
                byte* address;

                if (s_isEnhanced)
                {
                    // Matches calls from both _SET_TRANSMISSION_REDUCED_GEAR_RATIO and SET_VEHICLE_KERS_ALLOWED
                    // But they both use the same offset.
                    address = MemScanner.FindPatternBmh("85 ff 41 0f 95 c0 48 89 f1 48 81 c1");
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x49\x8B\xF1\x48\x8B\xF9\x0F\x57\xC0\x0F\x28\xF9\x0F\x28\xF2\x74\x4C", "xxxxxxxxxxxxxxxxx");
                }
                if (address != null)
                {
                    CVehicleEngineOffset = *(int*)(address + (s_isEnhanced? 12 : 0x24)); // 0x880
                    // Test these.
                    NextGearOffset = CVehicleEngineOffset;
                    GearOffset = CVehicleEngineOffset + 2;
                    HighGearOffset = CVehicleEngineOffset + 6;

                    if (s_isEnhanced)
                    {
                        // For some reason, the pattern I found initially (F3 0F 10 4C 24 78 F3 0F 10 57), which matches the one from legacy,
                        // cannot be found using our FindPattern. Using C.E., you can only find it if "Writable" is "gray".
                        // Manually finding the CVehicleEngineTurbo address (VehicleAddr + CVehicleEngineOffset + 0x7c (as it hasn't changed)),
                        // and finding out what accesses it, gives us a direct offset (VehicleAddr + 0x8fc),
                        // as well as many accesses using VehicleAddr + CVehicleEngineOffset + 0x7c.
                        // However, these cannot be found in the dump I'm using (814.9), even If I wildcard the used registers.
                        // For that reason, I calculate CVehicleEngineTurboOffset by subtracting CVehicleEngineOffset from the offset added to VehicleAddr.
                        address = MemScanner.FindPatternBmh("75 ? f3 0f 10 9e ? ? ? ? f3 0f 10 25");
                        int directTurboAccessOffset = *(int*)(address + 6); // 0x8fc
                        CVehicleEngineTurboOffset = directTurboAccessOffset - CVehicleEngineOffset; // 0x8fc - 0x880 = 0x7c
                    }
                    else
                    {
                        address = MemScanner.FindPatternBmh("\x0F\x28\xC3\xF3\x0F\x5C\xC2\x0F\x2F\xC8\x76\x2E\x0F\x2F\xDA\x73\x29\xF3\x0F\x10\x44\x24\x58", "xxxxxxxxxxxxxxxxxxxxxxx");
                        if (address != null)
                        {
                            CVehicleEngineTurboOffset = (int)*(byte*)(s_isEnhanced ? address + 10 : address - 1); // 0x7c
                        }
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("66 83 f8 ? 74 ? f3 0f 10 86 ? ? ? ? 0f 57 c9");
                    if (address != null)
                    {
                        FuelLevelOffset = *(int*)(address + 10); // 0x844
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x74\x26\x0F\x57\xC9\x0F\x2F\x8B\x34\x08\x00\x00\x73\x1A\xF3\x0F\x10\x83", "xxxxxxxx????xxxxxx");
                    if (address != null)
                    {
                        FuelLevelOffset = *(int*)(address + 8); // 0x844
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("74 ? f3 0f 10 81 ? ? ? ? 0f 57 c9 0f 2e c1 76");
                    if (address != null)
                    {
                        OilLevelOffset = *(int*)(address + 6); // 0x848
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x74\x2D\x0F\x57\xC0\x0F\x2F\x83", "xxxxxxxx");
                    if (address != null)
                    {
                        OilLevelOffset = *(int*)(address + 8); // 0x848
                    }
                }

                // Used by REQUEST_VEHICLE_DIAL
                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("f3 44 0f 10 86 ? ? ? ? f3 41 0f 5c c0 f3 0f 10 15");
                    if (address != null)
                    {
                        WheelSpeedOffset = *(int*)(address + 5);
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\xF3\x0F\x10\x8F\x10\x0A\x00\x00\xF3\x0F\x59\x05", "xxxx????xxxx");
                    if (address != null)
                    {
                        WheelSpeedOffset = *(int*)(address + 4);
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("4c 8b 89 ? ? ? ? 45 31 d2 0f 1f");
                    if (address != null)
                    {
                        WheelCountOffset = *(int*)(address - 9); // 0xc38
                        // TODO: Test these.
                        WheelPtrArrayOffset = WheelCountOffset - 8;
                        WheelBoneIdToPtrArrayIndexOffset = WheelCountOffset + 4;
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x48\x63\x99\x00\x00\x00\x00\x45\x33\xC0\x45\x8B\xD0\x48\x85\xDB", "xxx????xxxxxxxxx");
                    if (address != null)
                    {
                        WheelCountOffset = *(int*)(address + 3); // 0xc38
                        WheelPtrArrayOffset = WheelCountOffset - 8;
                        WheelBoneIdToPtrArrayIndexOffset = WheelCountOffset + 4;
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("85 ff 0f 94 c0 0f b6 8e ? ? ? ? c0 e0 06");
                    if (address != null)
                    {
                        CanWheelBreakOffset = *(int*)(address + 8);
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x74\x18\x80\xA0\x00\x00\x00\x00\xBF\x84\xDB\x0F\x94\xC1\x80\xE1\x01\xC0\xE1\x06", "xxxx????xxxxxxxxxxxx");
                    if (address != null)
                    {
                        CanWheelBreakOffset = *(int*)(address + 4);
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("f3 45 0f 10 8f ? ? ? ? 4c 89 f9");
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x76\x03\x0F\x28\xF0\xF3\x44\x0F\x10\x93", "xxxxxxxxxx");
                }
                if (address != null)
                {
                    CurrentRpmOffset = *(int*)(address + (s_isEnhanced ? 5 : 10)); // 0x8c8
                    // TODO: Test these for Enhanced.
                    ClutchOffset = CurrentRpmOffset + 0xC;
                    AccelerationOffset = CurrentRpmOffset + 0x10;
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("0f 56 f9 f3 0f 11 be ? ? ? ? 48 8b 86");
                }
                else
                {
                    // Ignore the register to read as the base address, it got changed from `rbx` to `rdi` in b3095
                    address = MemScanner.FindPatternBmh("\x74\x0A\xF3\x0F\x11\xB3\x1C\x09\x00\x00\xEB\x25", "xxxxx?????xx");
                }
                if (address != null)
                {
                    SteeringScaleOffset = *(int*)(address + (s_isEnhanced ? 7 : 6));
                    // TODO: Test these for Enhanced.
                    SteeringAngleOffset = SteeringScaleOffset + 8;
                    ThrottlePowerOffset = SteeringScaleOffset + 0x10;
                    BrakePowerOffset = SteeringScaleOffset + 0x14;
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("f3 0f 59 c6 f3 0f 58 c1 f3 0f 11 86 ? ? ? ? f6 86");
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\xF3\x0F\x11\x9B\xDC\x09\x00\x00\x0F\x84\xB1", "xxxx????xxx");
                }
                if (address != null)
                {
                    EngineTemperatureOffset = *(int*)(address + (s_isEnhanced? 12 : 4));
                }

                int gameVersion = GetGameVersion();

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("f3 0f 11 89 ? ? ? ? f3 0f 5e 35");
                    if (address != null)
                    {
                        // In Enhanced, only modifyVehicleTopSpeedOffset1 can be found, and EnginePowerMultiplierOffset is accessed directly.
                        EnginePowerMultiplierOffset = *(int*)(address + 4); // 0xb20
                    }
                }
                else
                {
                    // Get the offset that is stored by MODIFY_VEHICLE_TOP_SPEED if the game version is b944 or later for existing script compatibility
                    // MODIFY_VEHICLE_TOP_SPEED stores the 2nd argument value to CVehicle in b944 or later, but that's not the case in earlier ones
                    if (gameVersion >= 28)
                    {
                        address = MemScanner.FindPatternBmh("\x48\x89\x5C\x24\x28\x44\x0F\x29\x40\xC8\x0F\x28\xF9\x44\x0F\x29\x48\xB8\xF3\x0F\x11\xB9", "xxxxxxxxxxxxxxxxxxxxxx");
                        if (address != null)
                        {
                            int modifyVehicleTopSpeedOffset1 = *(int*)(address - 4); // 0x880
                            int modifyVehicleTopSpeedOffset2 = *(int*)(address + 22); // 0x2a0
                            EnginePowerMultiplierOffset = modifyVehicleTopSpeedOffset1 + modifyVehicleTopSpeedOffset2; // 0x880 + 0x2a0 == 0xb20
                        }
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("0f 84 ? ? ? ? 49 89 c4 80 88");
                    if (address != null)
                    {
                        DisablePretendOccupantOffset = *(int*)(address + 11);
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x48\x8B\xF8\x48\x85\xC0\x0F\x84\xE2\x00\x00\x00\x80\x88", "xxxxxxxxxxxxxx");
                    if (address != null)
                    {
                        DisablePretendOccupantOffset = *(int*)(address + 14);
                    }
                }

                if (s_isEnhanced)
                {
                    // This pattern points to an access to the same Byte SET_VEHICLE_PROVIDES_COVER accesses.
                    // The bitwise operations however don't access the same bit, because no unique pattern could be found for the command for SET_VEHICLE_PROVIDES_COVER.
                    // (SET_VEHICLE_PROVIDES_COVER accesses the 3rd Bit (Bit 2))
                    address = MemScanner.FindPatternBmh("80 a3 ? ? ? ? ? 0f b6 83 ? ? ? ? 89 c1");
                    if (address != null)
                    {
                        ProvidesCoverOffset = *(int*)(address + 10);
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x48\x83\xEC\x20\x49\x8B\xF8\x48\x8B\xDA\x48\x85\xD2\x74\x4A\x80\x7A\x28\x03\x75\x44\xF6\x82", "xxxxxxxxxxxxxxxxxxxxxxx");
                    if (address != null)
                    {
                        ProvidesCoverOffset = *(int*)(address + 23);
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("f3 44 0f 59 9d ? ? ? ? f3 44 0f 59 9a");
                    if (address != null)
                    {
                        LightsMultiplierOffset = *(int*)(address + 5); // 0xa3c
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x74\x03\x41\x8A\xF4\xF3\x44\x0F\x59\x93\x00\x00\x00\x00\x48\x8B\xCB\xF3\x44\x0F\x59\x97\xFC\x00\x00\x00\xF3\x44\x0F\x59\xD6", "xxxxxxxxxx????xxxxxxxxxxxxxxxxx");
                    if (address != null)
                    {
                        LightsMultiplierOffset = *(int*)(address + 10); // 0xa3c
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("22 96 ? ? ? ? 08 c2");
                    if (address != null)
                    {
                        IsInteriorLightOnOffset = *(int*)(address + 2);
                        // TODO: Test this for Enhnaced.
                        IsEngineStartingOffset = IsInteriorLightOnOffset + 1;
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\xFD\x02\xDB\x08\x98\x00\x00\x00\x00\x48\x8B\x5C\x24", "xxxxx????xxxx");
                    if (address != null)
                    {
                        IsInteriorLightOnOffset = *(int*)(address - 4);
                        IsEngineStartingOffset = IsInteriorLightOnOffset + 1;
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("4c 89 ea e8 ? ? ? ? 49 8d 8d");
                    if (address != null)
                    {
                        int unkVehHeadlightOffset = *(int*)(address + 11); // 0x420
                        byte* unkFuncAddr = (*(int*)(address + 16) + address + 20);
                        // This pattern is unique. Using a startAddress to scan for it simply for consistency with Legacy, as well as narrowing the search scope.
                        address = MemScanner.FindPatternBmh("8b 84 86 ? ? ? ? 0f a3 e8", new IntPtr(unkFuncAddr));
                        if (address != null)
                        {
                            IsHeadlightDamagedOffset = *(int*)(address + 3) + unkVehHeadlightOffset; // 0x43c
                        }
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x39\x00\x75\x0F\x48\xFF\xC1\x48\x83\xC0\x04\x48\x83\xF9\x02\x7C\xEF\xEB\x08\x48\x8B\xCF", "x?xxxxxxxxxxxxxxxxxxxx");
                    if (address != null)
                    {

                        address = MemScanner.FindPatternNaive("\x48\x8D\x8F\x00\x00\x00\x00\xE8\x00\x00\x00\x00\x8A\x87", "xxx????x????xx", new IntPtr(address - 0x50), 0x50u);
                        if (address != null)
                        {
                            int unkVehHeadlightOffset = *(int*)(address + 3); // 0x420
                            byte* unkFuncAddr = (*(int*)(address + 8) + address + 12);
                            address = MemScanner.FindPatternBmh("\x8B\xC3\x8B\xCB\x48\xC1\xE8\x05\x83\xE1\x1F", "xxxxxxxxxxx", new IntPtr(unkFuncAddr), 0x200u);
                            if (address != null)
                            {
                                IsHeadlightDamagedOffset = *(int*)(address + 14) + unkVehHeadlightOffset; // 0x43c
                            }
                        }
                    }
                }
                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("f3 0f 11 87 ? ? ? ? 0f b7 93");
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x8B\xCB\x89\x87\x90\x00\x00\x00\x8A\x86\xD8\x00\x00\x00\x24\x07\x41\x3A\xC6\x0F\x94\xC1\xC1\xE1\x12\x33\xCA\x81\xE1\x00\x00\x04\x00\x33\xCA", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
                }
                if (address != null)
                {
                    LodMultiplierOffset = *(int*)(address - 4);
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("41 f6 85 ? ? ? ? ? 75 ? 41 f6 85 ? ? ? ? ? 0f 85");
                    if (address != null)
                    {
                        IsWantedOffset = *(int*)(address + 13); // 0x97c
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x8A\x96\x00\x00\x00\x00\x0F\xB6\xC8\x84\xD2\x41", "xx????xxxxxx");
                    if (address != null)
                    {
                        IsWantedOffset = *(int*)(address + 40); // 0x97c
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("84 db 74 ? 80 a6 ? ? ? ? ? 48 89 f1");
                    if (address != null)
                    {
                        PreviouslyOwnedByPlayerOffset = *(int*)(address + 6); // 0x974
                        // TODO: Test this for Enhanced.
                        NeedsToBeHotwiredOffset = PreviouslyOwnedByPlayerOffset;
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x45\x33\xC9\x41\xB0\x01\x40\x8A\xD7", "xxxxxxxxx");
                    if (address != null)
                    {
                        PreviouslyOwnedByPlayerOffset = *(int*)(address - 5); // 0x974
                        NeedsToBeHotwiredOffset = PreviouslyOwnedByPlayerOffset;
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("66 83 be ? ? ? ? ? 75 ? 0f b7 86");
                }
                else
                {
                    address = MemScanner.FindPatternBmh("66 39 81 ? ? ? ? 75 ? 8a 81 ? ? ? ? 24");
                }
                if (address != null)
                {
                    AlarmTimeOffset = *(int*)(address + 3); // 0xae0
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("75 ? 0f b6 86 ? ? ? ? a8 ? 75 ? 0c ? 88 86 ? ? ? ? eb");
                    if (address != null)
                    {
                        HasMutedSirensOffset = *(int*)(address + 5); // 0x986
                        // Legacy has two bit masks, 0x10 and 0x20 used in IS_VEHICLE_SIREN_AUDIO_ON to test for 0.
                        // Enhanced however, adds both masks together (implicitly) into 0x30 and tests for 0.
                        // We can only find the second mask (0x20), and we scan for the 0x30 using HasMutedSirenOffset (so the pattern is unique).
                        // We then subtract them to get HasMutedSirenBit.
                        var offsetBytes = BitConverter.GetBytes(HasMutedSirensOffset);
                        // f6 87 86 09 00 00 ? 0f 85 ? ? ? ? e9
                        string patternString = $"f6 87 {offsetBytes[0]:X2} {offsetBytes[1]:X2} {offsetBytes[2]:X2} {offsetBytes[3]:X2} ? 0f 85 ? ? ? ? e9";
                        int secondMask = *(byte*)(address + 10); // 0x20
                        address = MemScanner.FindPatternBmh(patternString);
                        int totalMask = *(byte*)(address + 6); // 0x30
                        HasMutedSirensBit = totalMask - secondMask; // 0x30 - 0x20 == 0x10
                        CanUseSirenOffset = *(int*)(address - 23); // 0x96b
                    }
                }
                else
                {
                    address = MemScanner.FindPatternNaive("\x48\x85\xC0\x74\x32\x8A\x88\x00\x00\x00\x00\xF6\xC1\x00\x75\x27", "xxxxxxx????xx?xx");
                    if (address != null)
                    {
                        HasMutedSirensOffset = *(int*)(address + 7); // 0x986
                        // the bit is changed between b372 and b2802
                        HasMutedSirensBit = *(byte*)(address + 13); // 0x10
                        CanUseSirenOffset = *(int*)(address + 23); // 0x96b
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("41 80 be ? ? ? ? ?  0f 95 c1");
                    if (address != null)
                    {
                        ModelSirenIdOffset = *(int*)(address + 3); // 0x53b
                        SirenBufferOffset = *(int*)(address + 33); // 0x1380
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\xC1\xE9\x1C\xC0\xE1\x06\x32\xC8\x80\xE1\x40\x32\xC8\x88\x8E", "xxxxxxxxxxxxxxx");
                    if (address != null)
                    {
                        ModelSirenIdOffset = *(int*)(address + 0x1F); // 0x53b
                        SirenBufferOffset = *(int*)(address + 0x26); // 0x1380
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("41 8b 8c 24 ? ? ? ? 83 f9 ? 77 ? ba ? ? ? ? 0f a3 ca 73 ? 31 c9");
                    if (address != null)
                    {
                        VehicleTypeOffset = *(int*)(address + 4); // 0xc28
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x41\xBB\x07\x00\x00\x00\x8A\xC2\x41\x23\xCB\x41\x22\xC3\x3C\x03\x75\x16", "xxxxxxxxxxxxxxxxxx");
                    if (address != null)
                    {
                        VehicleTypeOffset = *(int*)(address - 0x1C); // 0xc28
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("75 ? 80 3d ? ? ? ? ? 75 ? 80 8e");
                    if (address != null)
                    {
                        DropsMoneyWhenBlownUpOffset = *(int*)(address + 13);
                    }
                }                
                else
                {
                    address = MemScanner.FindPatternBmh("\x83\xB8\x00\x00\x00\x00\x00\x77\x12\x80\xA0\x00\x00\x00\x00\xFD\x80\xE3\x01\x02\xDB\x08\x98", "xx?????xxxx????xxxxxxxx");
                    if (address != null)
                    {
                        DropsMoneyWhenBlownUpOffset = *(int*)(address + 11);
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("f3 0f 10 96 ? ? ? ? f3 0f 10 25");
                    if (address != null)
                    {
                        HeliBladesSpeedOffset = *(int*)(address + 4);
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x73\x1E\xF3\x41\x0F\x59\x86\x00\x00\x00\x00\xF3\x0F\x59\xC2\xF3\x0F\x59\xC7", "xxxxxxx????xxxxxxxx");
                    if (address != null)
                    {
                        HeliBladesSpeedOffset = *(int*)(address + 7);
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("f3 0f 11 86 ? ? ? ? f3 0f 10 8f ? ? ? ? b1");
                    if (address != null)
                    {
                        HeliMainRotorHealthOffset = *(int*)(address + 12);
                        HeliTailRotorHealthOffset = HeliMainRotorHealthOffset + 4;
                        HeliTailBoomHealthOffset = HeliMainRotorHealthOffset + 8;
                    }

                    // Alternative: 8b 87 ? ? ? ? 83 e0 fe 83 f8 08 0f 85 ? ? ? ? e9
                    // where you will find 7 matches, resolve their jmp, only 3 will have a MOVSS. take those, and take the offsets there.
                    // The smallest offset is mainRotorHealth, the middle one is tailRotorHealth and the biggest is tailBoomHealth.
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\xB3\x03\x22\xD3\x48\x8B\xCF\xE8\x00\x00\x00\x00\x48\x8B\xCF\xF3\x0F\x11\x86\x00\x00\x00\x00\x8A\x97\xD4\x00\x00\x00\xC0\xEA\x02\x22\xD3\xE8", "xxxxxxxx????xxxxxxx????xxxxxxxxxxxx");
                    if (address != null)
                    {
                        HeliMainRotorHealthOffset = *(int*)(address + 19);
                        HeliTailRotorHealthOffset = HeliMainRotorHealthOffset + 4;
                        HeliTailBoomHealthOffset = HeliMainRotorHealthOffset + 8;
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("48 83 c0 ? 48 8b 8b ? ? ? ? f3 0f 10 89");
                    if (address != null)
                    {
                        HandlingDataOffset = *(int*)(address + 7);
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x3C\x03\x0F\x85\x00\x00\x00\x00\x48\x8B\x41\x20\x48\x8B\x88", "xxxx????xxxxxxx");
                    if (address != null)
                    {
                        HandlingDataOffset = *(int*)(address + 22);
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("0f 29 54 24 ? e8 ? ? ? ? 48 85 c0");
                    if (address != null)
                    {
                        byte* unkHandlingFuncAddr = *(int*)(address + 6) + address + 10;
                        address = MemScanner.FindPatternBmh("0f b7 81", new IntPtr(unkHandlingFuncAddr));
                        if (address != null)
                        {
                            // TODO: Test that the -8 is correct for Enhnaced.
                            SubHandlingDataArrayOffset = (*(int*)(address + 3) - 8);
                        }
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x45\x33\xF6\x8B\xEA\x48\x8B\xF1\x41\x8B\xFE", "xxxxxxxxxxx");
                    if (address != null)
                    {
                        SubHandlingDataArrayOffset = (*(int*)(address + 15) - 8);
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("74 ? f6 80 ? ? ? ? ? 74 ? 80 be");
                    if (address != null)
                    {
                        FirstVehicleFlagsOffset = *(int*)(address + 4); // 0x57d
                    }
                }
                else
                {
                    address = MemScanner.FindPatternNaive("\x48\x85\xC0\x74\x3C\x8B\x80\x00\x00\x00\x00\xC1\xE8\x0F", "xxxxxxx????xxx");
                    if (address != null)
                    {
                        FirstVehicleFlagsOffset = *(int*)(address + 7); // 0x57c
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("f3 0f 59 04 88 f3 0f 59 86");
                    if (address != null)
                    {
                        CWheelFrontRearSelectorOffset = *(int*)(address + 9);
                        // TODO: Test this for Enhanced.
                        CWheelStaticForceOffset = CWheelFrontRearSelectorOffset - 4;
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\xF3\x0F\x59\x05\x00\x00\x00\x00\xF3\x0F\x59\x83\x00\x00\x00\x00\xF3\x0F\x10\xC8\x0F\xC6\xC9\x00", "xxxx????xxxx????xxxxxxxx");
                    if (address != null)
                    {
                        CWheelFrontRearSelectorOffset = *(int*)(address + 12);
                        CWheelStaticForceOffset = CWheelFrontRearSelectorOffset - 4;
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("0f 2e 05 ? ? ? ? 41 8b 8f");
                    if (address != null)
                    {
                        CWheelTireTemperatureOffset = *(int*)(address - 4);
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\xF3\x0F\x5C\xC8\x0F\x2F\xCB\xF3\x0F\x11\x89\x00\x00\x00\x00\x72\x10\xF3\x0F\x10\x1D", "xxxxxxxxxxx????xxxxxx");
                    if (address != null)
                    {
                        CWheelTireTemperatureOffset = *(int*)(address + 11);
                    }
                }

                // Used by IS_VEHICLE_TYRE_BURST
                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("f3 0f 10 80 ? ? ? ? 45 85 f6 0f 84 ? ? ? ? e9");
                    if (address != null)
                    {
                        CWheelTireHealthOffset = *(int*)(address + 4);
                        // TODO: Test this for Enhanced
                        CWheelSuspensionHealthOffset = CWheelTireHealthOffset - 4;
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x74\x13\x0F\x57\xC0\x0F\x2E\x80", "xxxxxxxx");
                    if (address != null)
                    {
                        CWheelTireHealthOffset = *(int*)(address + 8);
                        CWheelSuspensionHealthOffset = CWheelTireHealthOffset - 4;
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("e8 ? ? ? ? 80 bc 37 ? ? ? ? ? 74 ? 48 89 d9");
                    if (address != null)
                    {
                        byte* setTyreWearRateFuncAddr = *(int*)(address + 1) + address + 5;
                        CWheelTireWearRateOffset = *(int*)(setTyreWearRateFuncAddr + 4); // 0x1f0
                        // Same for Enhanced.
                        CWheelMaxGripDiffFromWearRateOffset = CWheelTireWearRateOffset + 4; // 0x1f4
                        CWheelWearRateScaleOffset = CWheelTireWearRateOffset + 8; // 0x1f8
                    }
                }
                else
                {
                    // the tire wear multipiler value for vehicle wheels is present only in b1868 or newer versions
                    if (gameVersion >= 54)
                    {
                        // In newer versions of the game, the offset can be found at +49 instead of +0x22.
                        // I am however not sure, which versions broke that.
                        // For that reason, I extended the old pattern which is unique.
                        // That should still make it unique, while not being present in the older unknown version,
                        // since there, it ends with the offset and not our pattern bytes.
                        // This way we can know what offset to use without knowing the exact version that broke the old offset.
                        address = MemScanner.FindPatternBmh("45 84 f6 74 ? f3 0f 59 0d ? ? ? ? f3 0f 10 83 ? ? ? ? f3 44 0f 10 8b ? ? ? ? f3 44 0f 5c ce f3 44 0f 59 f8");
                        if (address != null)
                        {
                            CWheelTireWearRateOffset = *(int*)(address + 49); // 0x1f0
                        }
                        else
                        {
                            address = MemScanner.FindPatternNaive("\x45\x84\xF6\x74\x08\xF3\x0F\x59\x0D\x00\x00\x00\x00\xF3\x0F\x10\x83", "xxxxxxxxx????xxxx");
                            if (address != null)
                            {
                                CWheelTireWearRateOffset = *(int*)(address + 0x22); // 0x1f0
                            }
                        }
                        if (address != null)
                        {
                            // The values for SET_TYRE_WEAR_RATE_SCALE and SET_TYRE_MAXIMUM_GRIP_DIFFERENCE_DUE_TO_WEAR_RATE are not present in b1868
                            if (gameVersion >= 59)
                            {
                                CWheelMaxGripDiffFromWearRateOffset = CWheelTireWearRateOffset + 4;
                                CWheelWearRateScaleOffset = CWheelTireWearRateOffset + 8;
                            }
                        }
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("4b 8b 04 d1 0f bf 88");
                    if (address != null)
                    {
                        WheelIdOffset = *(int*)(address + 7);
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x0F\xBF\x88\x00\x00\x00\x00\x3B\xCA\x74\x17", "xxx????xxxx");
                    if (address != null)
                    {
                        WheelIdOffset = *(int*)(address + 3);
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("48 8b 86 ? ? ? ? 48 8b 04 d8 8b 80 ? ? ? ? a8 ? 75 ? 40 f6 c5");
                    if (address != null)
                    {
                        CWheelDynamicFlagsOffset = *(int*)(address + 13);
                    }
                }
                else
                {
                    address = MemScanner.FindPatternNaive("\xEB\x02\x33\xC9\xF6\x81\x00\x00\x00\x00\x01\x75\x43", "xxxxxx????xxx");
                    if (address != null)
                    {
                        CWheelDynamicFlagsOffset = *(int*)(address + 6);
                    }
                }
                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("48 89 e9 e8 ? ? ? ? 41 80 a5");
                    if (address != null)
                    {
                        s_fixVehicleWheelFunc = (delegate* unmanaged[Stdcall]<IntPtr, void>)(new IntPtr(*(int*)(address + 4) + address + 8));
                    }
                    address = MemScanner.FindPatternBmh("48 8b 40 ? 80 a0 ? ? ? ? fd");
                    if (address != null)
                    {
                        ShouldShowOnlyVehicleTiresWithPositiveHealthOffset = *(int*)(address + 6); // 0x1a6
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x74\x21\x8B\xD7\x48\x8B\xCB\xE8\x00\x00\x00\x00\x48\x8B\xC8\xE8", "xxxxxxxx????xxxx");
                    if (address != null)
                    {
                        s_fixVehicleWheelFunc = (delegate* unmanaged[Stdcall]<IntPtr, void>)(new IntPtr(*(int*)(address + 16) + address + 20));
                        address = MemScanner.FindPatternNaive("\x80\xA1\x00\x00\x00\x00\xFD", "xx????x", new IntPtr(address + 20));
                        ShouldShowOnlyVehicleTiresWithPositiveHealthOffset = *(int*)(address + 2); // 0x16e
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("4c 8d 8c 24 ? ? ? ? e8 ? ? ? ? 4c 89 e9 e8");
                    s_punctureVehicleTireNewFunc = (delegate* unmanaged[Stdcall]<IntPtr, ulong, float, ulong, ulong, int, byte, bool, void>)(new IntPtr((long)(*(int*)(address + 9) + address + 13)));
                    s_burstVehicleTireOnRimNewFunc = (delegate* unmanaged[Stdcall]<IntPtr, void>)(new IntPtr((long)(*(int*)(address + 17) + address + 21)));
                }
                else
                {
                    // Vehicle Wheels has the owner vehicle pointer and new wheel functions are used since b1365
                    if (gameVersion >= 40)
                    {
                        address = MemScanner.FindPatternBmh("\x4C\x8B\x81\x28\x01\x00\x00\x0F\x29\x70\xE8\x0F\x29\x78\xD8", "xxxxxxxxxxxxxxx");
                        s_punctureVehicleTireNewFunc = (delegate* unmanaged[Stdcall]<IntPtr, ulong, float, ulong, ulong, int, byte, bool, void>)(new IntPtr((long)(address - 0x10)));
                        address = MemScanner.FindPatternBmh("\x48\x83\xEC\x50\x48\x8B\x81\x00\x00\x00\x00\x48\x8B\xF1\xF6\x80", "xxxxxxx????xxxxx");
                        s_burstVehicleTireOnRimNewFunc = (delegate* unmanaged[Stdcall]<IntPtr, void>)(new IntPtr((long)(address - 0xB)));
                    }
                    else
                    {
                        address = MemScanner.FindPatternBmh("\x41\xF6\x81\x00\x00\x00\x00\x20\x0F\x29\x70\xE8\x0F\x29\x78\xD8\x49\x8B\xF9", "xxx????xxxxxxxxxxxx");
                        s_punctureVehicleTireOldFunc = (delegate* unmanaged[Stdcall]<IntPtr, ulong, float, IntPtr, ulong, ulong, int, byte, bool, void>)(new IntPtr((long)(address - 0x14)));
                        address = MemScanner.FindPatternBmh("\x48\x83\xEC\x50\xF6\x82\x00\x00\x00\x00\x20\x48\x8B\xF2\x48\x8B\xE9", "xxxxxx????xxxxxxx");
                        s_burstVehicleTireOnRimOldFunc = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)(new IntPtr((long)(address - 0x10)));
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("f6 86 ? ? ? ? ? 75 ? f3 0f 10 86 ? ? ? ? 0f 2e 86");
                    if (address != null)
                    {
                        SpecialFlightTargetRatioOffset = *(int*)(address + 20); // 0x348
                        // SET_HOVER_MODE_WING_RATIO stores the ratio in both 0x34c and 0x35c in both Legacy and Enhanced
                        SpecialFlightWingRatioOffset = SpecialFlightTargetRatioOffset + 0x4; // 0x34c
                        SpecialFlightAreWingsDisabledOffset = SpecialFlightTargetRatioOffset + 0x1C;
                        SpecialFlightCurrentRatioOffset = SpecialFlightTargetRatioOffset + 0x28; // 0x370
                    }
                }
                else
                {
                    if (gameVersion >= 38)
                    {
                        address = MemScanner.FindPatternBmh("\x41\x0F\x2F\xC1\x72\x2E\xF6\x83", "xxxxxxxx");
                        if (address != null)
                        {
                            SpecialFlightTargetRatioOffset = *(int*)(address + 0x1C); // 0x348
                            SpecialFlightWingRatioOffset = SpecialFlightTargetRatioOffset + 0x4; // 0x34c
                            SpecialFlightAreWingsDisabledOffset = SpecialFlightTargetRatioOffset + 0x1C;
                            SpecialFlightCurrentRatioOffset = SpecialFlightTargetRatioOffset + 0x28; // 0x370
                        }
                    }
                }
                // The values for special flight mode (e.g. Deluxo) are present only in b1290 or later versions
            }

            public static IntPtr GetSubHandlingData(IntPtr handlingDataAddr, int handlingType)
            {
                var subHandlingArray = (RageAtArrayPtr*)(handlingDataAddr + SubHandlingDataArrayOffset);
                ushort subHandlingCount = subHandlingArray->size;
                if (subHandlingCount <= 0)
                {
                    return IntPtr.Zero;
                }

                for (int i = 0; i < subHandlingCount; i++)
                {
                    ulong subHandlingDataAddr = subHandlingArray->GetElementAddress(i);
                    if (subHandlingDataAddr == 0)
                    {
                        continue;
                    }

                    ulong vFuncAddr = *(ulong*)(*(ulong*)subHandlingDataAddr + (uint)0x10);
                    var getSubHandlingDataVFunc = (delegate* unmanaged[Stdcall]<ulong, int>)(vFuncAddr);
                    int handlingTypeOfCurrentElement = getSubHandlingDataVFunc(subHandlingDataAddr);
                    if (handlingTypeOfCurrentElement == handlingType)
                    {
                        return new IntPtr((long)subHandlingDataAddr);
                    }
                }

                return IntPtr.Zero;
            }

            public static float GetTurbo(int handle)
            {
                if (CVehicleEngineTurboOffset == 0)
                {
                    return 0f;
                }

                byte* vehEngineStructAddr = GetCVehicleEngine(handle);

                if (vehEngineStructAddr == null)
                {
                    return 0f;
                }

                return *(float*)(vehEngineStructAddr + CVehicleEngineTurboOffset);
            }

            // This only makes sense if you use SetTurbo every frame, or nop the other instructions (3 as per 889.22) that write to CVehicleEngineTurbo,
            // as the game seems to write to this address every frame, as long as the vehicle has turbo.
            public static void SetTurbo(int handle, float value)
            {
                if (CVehicleEngineTurboOffset == 0)
                {
                    return;
                }

                byte* vehEngineStructAddr = GetCVehicleEngine(handle);

                if (vehEngineStructAddr == null)
                {
                    return;
                }

                *(float*)(vehEngineStructAddr + CVehicleEngineTurboOffset) = value;
            }

            private static byte* GetCVehicleEngine(int handle)
            {
                IntPtr address = GetEntityAddress(handle);

                if (address == IntPtr.Zero)
                {
                    return null;
                }

                return (byte*)(address + CVehicleEngineOffset);
            }

            public static int SpecialFlightTargetRatioOffset { get; }
            public static int SpecialFlightWingRatioOffset { get; }
            public static int SpecialFlightCurrentRatioOffset { get; }
            public static int SpecialFlightAreWingsDisabledOffset { get; }

            public static bool HasMutedSirens(int vehicleHandle)
            {
                IntPtr address = GetEntityAddress(vehicleHandle);

                if (address == IntPtr.Zero)
                {
                    return false;
                }

                return (*(byte*)(address + HasMutedSirensOffset) & HasMutedSirensBit) != 0;
            }

            public static bool HasSiren(int vehicleHandle)
            {
                IntPtr address = GetEntityAddress(vehicleHandle);

                if (address == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr modelAddress = GetModelInfo(address);

                if (modelAddress == IntPtr.Zero)
                {
                    return false;
                }

                // The siren id must not be zero to use the siren
                return (*(ulong**)(address + SirenBufferOffset) != null) && GetByteSirenIdOfVehicleModel(modelAddress) != 0;
            }

            public static int GetByteSirenIdOfVehicleModel(IntPtr vehicleModelAddress)
            {
                // This implementation doesn't consider real siren ID values generated by SirenSetting Limit Adjuster by cp702, but no need to consider as valid siren IDs will not be zero even with the adjuster installed
                // Raw carcols.meta and carvariations.meta files must be used for siren ID that exceeds 0xFF since carcols.ymt and carvariations.ymt can contain siren ID as uint8_t value
                // (SirenSetting Limit Adjuster modifies the siren ID type to uint16_t type during runtime)
                // Do not complain CodeWalker for carcols.ymt and carvariations.ymt not supporting siren ID that exceeds 0xFF, that is an expected behavior
                return *(byte*)(vehicleModelAddress + ModelSirenIdOffset);
            }

            #endregion

            #region -- Vehicle Wheel Data --

            private static delegate* unmanaged[Stdcall]<IntPtr, void> s_fixVehicleWheelFunc;
            private static delegate* unmanaged[Stdcall]<IntPtr, ulong, float, ulong, ulong, int, byte, bool, void> s_punctureVehicleTireNewFunc;
            private static delegate* unmanaged[Stdcall]<IntPtr, ulong, float, IntPtr, ulong, ulong, int, byte, bool, void> s_punctureVehicleTireOldFunc;
            private static delegate* unmanaged[Stdcall]<IntPtr, void> s_burstVehicleTireOnRimNewFunc;
            private static delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void> s_burstVehicleTireOnRimOldFunc;

            // Although we're sure the name of corresponding field in the exe is `m_fFrontRearSelector`, we don't know
            // what this value exactly affects...
            public static int CWheelFrontRearSelectorOffset { get; }

            public static int CWheelStaticForceOffset { get; }

            public static int CWheelTireTemperatureOffset { get; }

            public static int CWheelSuspensionHealthOffset { get; }

            public static int CWheelTireHealthOffset { get; }

            public static int CWheelTireWearRateOffset { get; }
            /// <summary>
            /// This value only affects how fast a vehicle tire health decreases, which is different from
            /// <see cref="CWheelTireWearRateOffset"/>.
            /// </summary>
            public static int CWheelMaxGripDiffFromWearRateOffset { get; }
            public static int CWheelWearRateScaleOffset { get; }

            /// <summary>
            /// The offset for the flag on CWheel where the "on fire" flag and "is touching" flag are set.
            /// </summary>
            public static int CWheelDynamicFlagsOffset { get; }

            public static int WheelIdOffset { get; }

            public static int ShouldShowOnlyVehicleTiresWithPositiveHealthOffset { get; }

            public static void FixVehicleWheel(IntPtr wheelAddress) => s_fixVehicleWheelFunc(wheelAddress);

            public static IntPtr GetVehicleWheelAddressByIndexOfWheelArray(IntPtr vehicleAddress, int index)
            {
                ulong* vehicleWheelArrayAddr = *(ulong**)(vehicleAddress + WheelPtrArrayOffset);

                if (vehicleWheelArrayAddr == null)
                {
                    return IntPtr.Zero;
                }

                return new IntPtr((long)*(vehicleWheelArrayAddr + index));
            }

            public static bool IsWheelTouchingSurface(IntPtr wheelAddress, IntPtr vehicleAddress)
            {
                if (CWheelDynamicFlagsOffset == 0)
                {
                    return false;
                }

                uint wheelTouchingFlag = *(uint*)(wheelAddress + CWheelDynamicFlagsOffset).ToPointer();
                if ((wheelTouchingFlag & 1) != 0)
                {
                    return true;
                }

                // Although `CWheel::GetIsTouching(CWheel *this)` only checks a certain flag in the `CWheel` instance
                // (the same check done above), we do the "slower check" for compatibilities for v3 scripts built
                // against v3.6.0 or earlier versions unless we confirm that removing the slower one doesn't break
                // the compatibilities.
                #region Slower Check
                if (((wheelTouchingFlag >> 1) & 1) == 0)
                {
                    return false;
                }

                ulong phCollider = *(ulong*)(*(ulong*)(vehicleAddress + 0x50).ToPointer() + 0x50);
                if (phCollider == 0)
                {
                    return true;
                }

                ulong unkStructAddr = *(ulong*)(phCollider + 0x18);
                if (unkStructAddr == 0)
                {
                    return false;
                }

                return (*(uint*)(unkStructAddr + 0x14) & 0xFFFFFFFD) == 0;
                #endregion
            }

            private static bool VehicleWheelHasVehiclePtr() => s_isEnhanced || GetGameVersion() >= 40;

            public static void PunctureTire(IntPtr wheelAddress, float damage, IntPtr vehicleAddress)
            {
                var task = new VehicleWheelPunctureTask(wheelAddress, vehicleAddress, false, damage);
                ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);
            }

            public static void BurstTireOnRim(IntPtr wheelAddress, IntPtr vehicleAddress)
            {
                var task = new VehicleWheelPunctureTask(wheelAddress, vehicleAddress, true);
                ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);
            }

            // the function BurstVehicleTireOnRimNew(Old)Func calls must be called in the main thread or the game will crash
            // the function PunctureVehicleTireNew(Old)Func calls should be called in the main thread or the game might crash in some cases
            internal sealed class VehicleWheelPunctureTask : IScriptTask
            {
                #region Fields

                private IntPtr _wheelAddress;
                private IntPtr _vehicleAddress;
                private bool _burstWheelCompletely;
                private float _damage;
                #endregion

                internal VehicleWheelPunctureTask(IntPtr wheelAddress, IntPtr vehicleAddress, bool burstWheelCompletely, float damage = 1000f)
                {
                    this._wheelAddress = wheelAddress;
                    this._vehicleAddress = vehicleAddress;
                    this._burstWheelCompletely = burstWheelCompletely;
                    this._damage = damage;
                }

                public void Run()
                {
                    int outValInt;
                    float outValFloat;

                    if (VehicleWheelHasVehiclePtr())
                    {
                        s_punctureVehicleTireNewFunc(_wheelAddress, 0, _damage, (ulong)&outValInt, (ulong)&outValFloat, 3, 0, true);
                        if (_burstWheelCompletely)
                        {
                            s_burstVehicleTireOnRimNewFunc(_wheelAddress);
                        }
                    }
                    else
                    {
                        s_punctureVehicleTireOldFunc(_wheelAddress, 0, _damage, _vehicleAddress, (ulong)&outValInt, (ulong)&outValFloat, 3, 0, true);
                        if (_burstWheelCompletely)
                        {
                            s_burstVehicleTireOnRimOldFunc(_wheelAddress, _vehicleAddress);
                        }
                    }
                }
            }

            #endregion
        }


        #region -- Prop Data --

        private static int s_objParentEntityAddressDetachedFromOffset;

        private static IntPtr GetParentEntityOfPropDetachedFrom(int objHandle)
        {
            IntPtr entityAddress = GetEntityAddress(objHandle);
            if (s_objParentEntityAddressDetachedFromOffset == 0 || entityAddress == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return new IntPtr(*(long*)(entityAddress + s_objParentEntityAddressDetachedFromOffset));
        }
        public static int GetParentEntityHandleOfPropDetachedFrom(int objHandle)
        {
            IntPtr parentEntityAddr = GetParentEntityOfPropDetachedFrom(objHandle);
            if (parentEntityAddr == IntPtr.Zero)
            {
                return 0;
            }

            return GetEntityHandleFromAddress(parentEntityAddr);
        }
        public static bool HasPropBeenDetachedFromParentEntity(int objHandle) => GetParentEntityOfPropDetachedFrom(objHandle) != IntPtr.Zero;

        #endregion -- Prop Data --

        public static unsafe class Ped
        {
            static Ped()
            {
                byte* address;
                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("75 ? 48 8b 8f ? ? ? ? 89 f2 48 83 c4");
                    if (address != null)
                    {
                        PedIntelligenceOffset = *(int*)(address + 5); // 0x10a0
                        byte* setDecisionMakerHashFuncAddr = *(int*)(address + 18) + address + 22;
                        PedIntelligenceDecisionMakerHashOffset = *(int*)(setDecisionMakerHashFuncAddr + 24); // 0x4d0
                        IntentoryOfCPedOffset = PedIntelligenceOffset + 0x10;
                        UnkStateOffset = PedIntelligenceOffset - 0x10;
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x48\x85\xC0\x74\x7F\xF6\x80\x00\x00\x00\x00\x02\x75\x76", "xxxxxxx????xxx");
                    if (address != null)
                    {
                        PedIntelligenceOffset = *(int*)(address + 0x11);

                        byte* setDecisionMakerHashFuncAddr = *(int*)(address + 0x18) + address + 0x1C;
                        PedIntelligenceDecisionMakerHashOffset = *(int*)(setDecisionMakerHashFuncAddr + 0x1C);

                        IntentoryOfCPedOffset = PedIntelligenceOffset + 0x10;
                        UnkStateOffset = PedIntelligenceOffset - 0x10;
                    }
                }
                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("48 8b 8b ? ? ? ? 48 8b 01 41 b8 ? ? ? ? 41 b1");
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x48\x8B\x88\x00\x00\x00\x00\x48\x85\xC9\x74\x43\x48\x85\xD2", "xxx????xxxxxxxx");
                }
                if (address != null)
                {
                    CTaskTreePedOffset = *(int*)(address + 3);
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("8b ad ? ? ? ? 80 3d");
                    if (address != null)
                    {
                        CEventCountOffset = *(int*)(address + 2); // 0x438
                        address = MemScanner.FindPatternBmh("48 8b 9c c8", new IntPtr(address));
                        CEventStackOffset = *(int*)(address + 4); // 0x3b0
                    }
                }
                else
                {
                    address = MemScanner.FindPatternNaive("\x40\x38\x3D\x00\x00\x00\x00\x8B\xB6\x00\x00\x00\x00\x74\x0C", "xxx????xx????xx");
                    if (address != null)
                    {
                        CEventCountOffset = *(int*)(address + 9); // 0x438
                        address = MemScanner.FindPatternNaive("\x48\x8B\xB4\xC6", "xxxx", new IntPtr(address));
                        CEventStackOffset = *(int*)(address + 4); // 0x3b0
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("48 8b 81 ? ? ? ? 48 85 c0 74 ? 44 8b 40");
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x74\x51\x48\x8D\x81\x00\x00\x00\x00\x48\x85\xC0\x74\x43\x48\x8B\x00\x48\x85\xC0\x74\x0A", "xxxxx????xxxxxxxxxxxxx");
                }
                if (address != null)
                {
                    PedIntelligenceCTaskInfoOffset = *(int*)(address + (s_isEnhanced? 3 : 5));
                    PedIntelligenceCombatTargetPedAddressOffset = PedIntelligenceCTaskInfoOffset + 0x18;
                    PedIntelligenceCurrentScriptTaskHashOffset = PedIntelligenceCTaskInfoOffset + 0x20;
                    PedIntelligenceCurrentScriptTaskStatusOffset = PedIntelligenceCTaskInfoOffset + 0x24;
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("b3 ? f6 87 ? ? ? ? ? 74 ? 48 8b 47");
                    if (address != null)
                    {
                        int fragInstNmGtaOffset = *(int*)(address + 23); // 0x1430
                        KnockOffVehicleTypeOffset = s_fragInstNmGtaOffset + 0xC;
                    }
                }
                else
                {
                    address = MemScanner.FindPatternNaive("\x48\x83\xEC\x28\x48\x8B\x42\x00\x48\x85\xC0\x74\x09\x48\x3B\x82\x00\x00\x00\x00\x74\x21", "xxxxxxx?xxxxxxxx????xx");
                    if (address != null)
                    {
                        int fragInstNmGtaOffset = *(int*)(address + 16); // 0x1430
                        KnockOffVehicleTypeOffset = s_fragInstNmGtaOffset + 0xC;
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("80 bb ? ? ? ? ? 0f 84 ? ? ? ? 48 8d 0d");
                    if (address != null)
                    {
                        CPed__PedResetFlagsOffset = *(int*)(address + 3); // 0x1480
                    }
                }
                else
                {
                    // Find a piece of code inside `CTaskMotionBase::CalcVelChangeLimitAndClamp(const CPed& ped,
                    // Vec3V_In changeInV, ScalarV_In timestepV, const CPhysical* pGroundPhysical)`.
                    // The cmp instruction is a part of the compiled code of CPhysical::GetIsTypeVehicle() call on
                    // `pGroundPhysical`, and the internal enum map of `ENTITY_TYPE_*` (`eEntityType`) is not likely to be
                    // changed by game updates.
                    address = MemScanner.FindPatternBmh("\x76\x20\x48\x85\xFF\x74\x1B\x80\x7F\x28\x03\x75\x15\x48\x8B\xCF", "xxxxxxxxxxxxxxxx");
                    if (address != null)
                    {
                        CPed__PedResetFlagsOffset = *(int*)(address + 0x24); // 0x1480
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("f3 0f 10 99 ? ? ? ? 0f 57 e4 0f 2e e3");
                }
                else
                {
                    address = MemScanner.FindPatternBmh("75 ? f3 0f 10 81 ? ? ? ? 0f 2f c2");
                }
                if (address != null)
                {
                    LowerWetnessLevelOffset = *(int*)(address + (s_isEnhanced? 4 : 6)); // 0x2f8
                    UpperWetnessLevelOffset = LowerWetnessLevelOffset + 4;
                    LowerWetnessHeightOffset = LowerWetnessLevelOffset - 8;
                    UpperWetnessHeightOffset = LowerWetnessLevelOffset - 4;
                    IsUsingWetEffectOffset = *(int*)(address - (s_isEnhanced ? 12 : 10)); // 0x15e0
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("0f 56 c8 f3 0f 10 05 ? ? ? ? f3 0f 10 96");
                    if (address != null)
                    {
                        SweatOffset = *(int*)(address + 15); // 0x11a0
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x0F\x93\xC0\x84\xC0\x74\x0F\xF3\x41\x0F\x58\xD1\x41\x0F\x2F\xD0\x72\x04\x41\x0F\x28\xD0", "xxxxxxxxxxxxxxxxxxxxxx");
                    if (address != null)
                    {
                        SweatOffset = *(int*)(address + 26); // 0x11a0
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("8b 82 ? ? ? ? a9 ? ? ? ? 74 ? 48 83 bb");
                    if (address != null)
                    {
                        IsInVehicleOffset = *(int*)(address + 2); // 0x1448
                        LastVehicleOffset = *(int*)(address + 16); // 0x1530
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x8A\x41\x30\xC0\xE8\x03\xA8\x01\x74\x49\x8B\x82", "xxxxxxxxxxxx");
                    if (address != null)
                    {
                        IsInVehicleOffset = *(int*)(address + 12); // 0x1448
                        LastVehicleOffset = *(int*)(address + 0x1A); // 0x1530
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("41 0f b6 46 ? 83 e0 ? 66 89 86");
                    if (address != null)
                    {
                        AttachCarSeatIndexOffset = *(int*)(address + 11); // 0x15d8
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x24\x3F\x0F\xB6\xC0\x66\x89\x87", "xxxxxxxx");
                    if (address != null)
                    {
                        AttachCarSeatIndexOffset = *(int*)(address + 8); // 0x15d8
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("0f 84 ? ? ? ? 49 8d 8f ? ? ? ? 4c 89 ea");
                    if (address != null)
                    {
                        GroundPhysicalOffset = *(int*)(address + 9); // 0x1578
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x84\xC0\x0F\x84\x2C\x01\x00\x00\x48\x8D\x9F\x00\x00\x00\x00\x48\x8B\x0B\x48\x3B\xCE\x74\x1B\x48\x85\xC9\x74\x08\x48\x8B\xD3\xE8", "xxxxxxxxxxx????xxxxxxxxxxxxxxxxx");
                    if (address != null)
                    {
                        GroundPhysicalOffset = *(int*)(address + 11); // 0x1578
                    }
                }

                if (s_isEnhanced)
                {
                    // From SET_PED_DROPS_WEAPONS_WHEN_DEAD
                    address = MemScanner.FindPatternBmh("23 8e ? ? ? ? c1 e0 0e 09 c8");
                    if (address != null)
                    {
                        DropsWeaponsWhenDeadOffset = *(int*)(address + 2); // 0x1444
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\xC1\xE8\x11\xA8\x01\x75\x10\x48\x8B\xCB\xE8\x00\x00\x00\x00\x84\xC0\x0F\x84", "xxxxxxxxxxx????xxxx");
                    if (address != null)
                    {
                        DropsWeaponsWhenDeadOffset = *(int*)(address - 4); // 0x1444
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("0f 94 c0 8b 8e ? ? ? ? 83 e1 ? 8d 04 81 89 86");
                    if (address != null)
                    {
                        SuffersCriticalHitOffset = *(int*)(address + 5); // 0x1444
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x4D\x8B\xF1\x48\x8B\xFA\xC1\xE8\x02\x48\x8B\xF1\xA8\x01\x0F\x85\xEB\x00\x00\x00", "xxxxxxxxxxxxxxxxxxxx");
                    if (address != null)
                    {
                        SuffersCriticalHitOffset = *(int*)(address - 4); // 0x1444
                    }
                }

                int gameVersion = GetGameVersion();

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("41 0f 2e 80 ? ? ? ? 0f 97 c0 eb"); 
                    if (address != null)
                    {
                        ArmorOffset = *(int*)(address + 4); // 0x150c
                        InjuryHealthThresholdOffset = ArmorOffset + 0xC;
                        FatalInjuryHealthThresholdOffset = ArmorOffset + 0x10;
                    }
                }
                else
                {
                    // Implementation of armor system was different drastically in the game version between b877 and b2699 and the other versions
                    if (gameVersion >= 80 || gameVersion <= 25)
                    {
                        address = MemScanner.FindPatternBmh("\x66\x0F\x6E\xC1\x0F\x5B\xC0\x41\x0F\x2F\x86\x00\x00\x00\x00\x0F\x97\xC0\xEB\x02\x32\xC0\x48\x8B\x5C\x24\x40", "xxxxxxxxxxx????xxxxxxxxxxxx");
                        if (address != null)
                        {
                            ArmorOffset = *(int*)(address + 11); // 0x150c
                            InjuryHealthThresholdOffset = ArmorOffset + 0xC;
                            FatalInjuryHealthThresholdOffset = ArmorOffset + 0x10;
                        }
                    }
                    else
                    {
                        address = MemScanner.FindPatternBmh("\x0F\x29\x70\xD8\x0F\x29\x78\xC8\x49\x8B\xF0\x48\x8B\xEA\x4C\x8B\xF1\x44\x0F\x29\x40\xB8", "xxxxxxxxxxxxxxxxxxxxxx");
                        if (address != null)
                        {
                            ArmorOffset = *(int*)(address + 0x1F);
                        }

                        // Injury healths value are stored with different distance from the armor value in different game versions, search for another pattern to make sure we get correct injury health offsets
                        address = MemScanner.FindPatternBmh("\xF3\x41\x0F\x10\x16\xF3\x0F\x10\xA7\xA0\x02\x00\x00\x0F\x28\xC3\xF3\x0F\x5C\xC2\x0F\x2F\xC6\x72\x05\x0F\x28\xCE\xEB\x12", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
                        if (address != null)
                        {
                            InjuryHealthThresholdOffset = *(int*)(address - 4);
                            FatalInjuryHealthThresholdOffset = InjuryHealthThresholdOffset + 0x4;
                        }
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("2b 83 ? ? ? ? 0f 86");
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x8B\x83\x00\x00\x00\x00\x8B\x35\x00\x00\x00\x00\x3B\xF0\x76\x04", "xx????xx????xxxx");
                }
                if (address != null)
                {
                    TimeOfDeathOffset = *(int*)(address + 2); // 0x160c
                    CauseOfDeathOffset = TimeOfDeathOffset - 4; // 0x1608
                    SourceOfDeathOffset = TimeOfDeathOffset - 12; // 0x1600
                }

                if (s_isEnhanced)
                {
                    // From SET_PED_FIRING_PATTERN
                    address = MemScanner.FindPatternBmh("89 b1 ? ? ? ? 48 81 c1 ? ? ? ? 89 f2");
                    if (address != null)
                    {
                        FiringPatternOffset = *(int*)(address + 2);
                    }
                }
                else
                {
                    address = MemScanner.FindPatternNaive("\x74\x08\x8B\x81\x00\x00\x00\x00\xEB\x0D\x48\x8B\x87\x00\x00\x00\x00\x8B\x80", "xxxx????xxxxx????xx");
                    if (address != null)
                    {
                        FiringPatternOffset = *(int*)(address + 19);
                    }
                }

                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("f3 0f 10 80 ? ? ? ? f3 0f 58 05 ? ? ? ? f3 0f 5f f0");
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\xC1\xE8\x09\xA8\x01\x74\xAE\x0F\x28\xA3\x00\x00\x00\x00\x49\x8B\x47\x30\xF3\x0F\x10\x81", "xxxxxxxxxx??xxxxxxxxxx");
                }
                if (address != null)
                {
                    SeeingRangeOffset = *(int*)(address + (s_isEnhanced? 4 : 0x16)); // 0xcfc
                    HearingRangeOffset = SeeingRangeOffset - 4; // 0xcf8
                    VisualFieldPeripheralRangeOffset = SeeingRangeOffset + 4; // 0xd00
                    VisualFieldMinAngleOffset = SeeingRangeOffset + 8;
                    VisualFieldMaxAngleOffset = SeeingRangeOffset + 0xC;
                    VisualFieldMinElevationAngleOffset = SeeingRangeOffset + 0x10;
                    VisualFieldMaxElevationAngleOffset = SeeingRangeOffset + 0x14;
                    VisualFieldCenterAngleOffset = SeeingRangeOffset + 0x18;
                }
            }

            #region -- CPed Data --

            /// <summary>
            /// <para>Gets the last vehicle the ped used or is using.</para>
            /// <para>
            /// This method exists because there are no reliable ways with native functions.
            /// The native <c>GET_VEHICLE_PED_IS_IN</c> returns the last vehicle the ped used when not in vehicle (though the 2nd parameter name is supposed to be <c>ConsiderEnteringAsInVehicle</c> as a leaked header suggests),
            /// but returns <c>0</c> when the ped is going to a door of some vehicle or opening one.
            /// Also, the native returns the vehicle's handle the ped is getting in when ped is getting in it, which is different from the last vehicle this method returns and the native <c>RESET_PED_LAST_VEHICLE</c> changes.
            /// </para>
            /// </summary>
            public static int GetLastVehicleHandle(IntPtr pedAddress)
            {
                if (LastVehicleOffset == 0)
                {
                    return 0;
                }

                var lastVehicleAddress = new IntPtr(*(long*)(pedAddress + LastVehicleOffset));
                return lastVehicleAddress != IntPtr.Zero ? GetEntityHandleFromAddress(lastVehicleAddress) : 0;
            }

            /// <summary>
            /// <para>Gets the current vehicle the ped is using.</para>
            /// <para>
            /// This method exists because <c>GET_VEHICLE_PED_IS_IN</c> always returns the last vehicle without checking the driving flag in b2699 even when the 2nd argument is set to <c>false</c> unlike previous versions.
            /// </para>
            /// </summary>
            public static int GetVehicleHandlePedIsIn(IntPtr pedAddress)
            {
                if (IsInVehicleOffset == 0 || LastVehicleOffset == 0)
                {
                    return 0;
                }

                uint bitFlags = *(uint*)(pedAddress + IsInVehicleOffset);
                bool isPedInVehicle = ((bitFlags & (1 << 0x1E)) != 0);
                if (!isPedInVehicle)
                {
                    return 0;
                }

                var lastVehicleAddress = new IntPtr(*(long*)(pedAddress + LastVehicleOffset));
                return lastVehicleAddress != IntPtr.Zero ? GetEntityHandleFromAddress(lastVehicleAddress) : 0;
            }

            /// <summary>
            /// Gets the physical entity handle the ped is standing on.
            /// </summary>
            public static int GetGroundPhysicalOfCPed(IntPtr pedAddress)
            {
                if (GroundPhysicalOffset == 0)
                {
                    return 0;
                }

                var groundPhysicalAddress = new IntPtr(*(long*)(pedAddress + GroundPhysicalOffset));
                return groundPhysicalAddress != IntPtr.Zero ? GetEntityHandleFromAddress(groundPhysicalAddress) : 0;
            }

            #endregion

            #region -- Ped Offsets --

            public static int LowerWetnessHeightOffset { get; }
            public static int UpperWetnessHeightOffset { get; }
            public static int LowerWetnessLevelOffset { get; }
            public static int UpperWetnessLevelOffset { get; }

            public static int IsUsingWetEffectOffset { get; }

            public static int SweatOffset { get; }

            /// <summary>
            /// The value at this offset should be 2 if the ped is a player ped.
            /// </summary>
            public static int UnkStateOffset { get; }

            public static int IntentoryOfCPedOffset { get; }

            // the same offset as the offset for SET_PED_CAN_BE_TARGETTED
            public static int DropsWeaponsWhenDeadOffset { get; }

            public static int SuffersCriticalHitOffset { get; }

            public static int ArmorOffset { get; }

            public static int InjuryHealthThresholdOffset { get; }
            public static int FatalInjuryHealthThresholdOffset { get; }

            public static int IsInVehicleOffset { get; }
            public static int LastVehicleOffset { get; }
            /// <summary>
            /// Contains the offset of <c>CPed.m_nAttachCarSeatIndex</c>, which is supposed to be <c>int16_t</c>.
            /// </summary>
            public static int AttachCarSeatIndexOffset { get; }

            /// <summary>
            /// Contains the offset of <c>CPed.m_pGroundPhysical</c>, which is supposed to be
            /// <c>rage::fwRegdRef&lt;CPhysical,rage::fwRefAwareBase&gt;</c> (a pointer).
            /// </summary>
            /// <remarks>
            /// The next field should be <c>CPed.m_pLastValidGroundPhysical</c>.
            /// </remarks>
            public static int GroundPhysicalOffset { get; }

            public static int SourceOfDeathOffset { get; }
            public static int CauseOfDeathOffset { get; }
            public static int TimeOfDeathOffset { get; }

            public static int KnockOffVehicleTypeOffset { get; }

            /// <summary>
            /// This offset is for `<c>CPed::m_PedResetFlags</c>`, not the offset of bit sets of reset flags is stored
            /// on `<c>CPed</c>` (as a `<c>CPedResetFlags::ePedResetFlagsBitSet</c>`).
            /// </summary>
            public static int CPed__PedResetFlagsOffset { get; }

            #region -- Ped Intelligence Offsets --

            public static int PedIntelligenceOffset { get; }

            public static int FiringPatternOffset { get; }

            public static int PedIntelligenceDecisionMakerHashOffset { get; }

            public static int SeeingRangeOffset { get; }
            public static int HearingRangeOffset { get; }
            public static int VisualFieldMinAngleOffset { get; }
            public static int VisualFieldMaxAngleOffset { get; }
            public static int VisualFieldMinElevationAngleOffset { get; }
            public static int VisualFieldMaxElevationAngleOffset { get; }
            public static int VisualFieldPeripheralRangeOffset { get; }
            public static int VisualFieldCenterAngleOffset { get; }

            public static int CTaskTreePedOffset { get; }

            public static int CEventCountOffset { get; }

            public static int CEventStackOffset { get; }

            public static int PedIntelligenceCTaskInfoOffset { get; }

            public static int PedIntelligenceCombatTargetPedAddressOffset { get; }

            public static int PedIntelligenceCurrentScriptTaskHashOffset { get; }
            public static int PedIntelligenceCurrentScriptTaskStatusOffset { get; }

            #endregion

            #endregion

            #region -- CPedIntelligence Data --

            public static IntPtr GetCPedIntelligence(IntPtr pedAddress)
                => PedIntelligenceOffset != 0 ? new IntPtr(*(long*)(pedAddress + PedIntelligenceOffset)) : IntPtr.Zero;

            public static int GetCombatTargetPedHandleFromCombatPed(IntPtr pedAddress)
            {
                if (PedIntelligenceCombatTargetPedAddressOffset == 0)
                {
                    return 0;
                }

                IntPtr pedIntelligence = GetCPedIntelligence(pedAddress);
                if (pedIntelligence == IntPtr.Zero)
                {
                    return 0;
                }

                // Actually, the game tests the value at [CTaskInfo + 0x8] using the AND and shr bitwise operations
                // and tests if the value at [CTaskInfo + 0xC] is the task index for CTaskCombat before accessing the member of the target ped pointer
                // In practice, however, it looks like we can use the target address without testing the 2 checks

                var targetPedAddress = new IntPtr(*(long*)(pedIntelligence + PedIntelligenceCombatTargetPedAddressOffset));
                if (targetPedAddress == IntPtr.Zero)
                {
                    return 0;
                }

                return GetEntityHandleFromAddress(targetPedAddress);
            }

            public static int GetCombatTargetPedHandleFromCombatPed(int pedHandle)
            {
                if (PedIntelligenceCombatTargetPedAddressOffset == 0)
                {
                    return 0;
                }

                IntPtr pedAddress = GetEntityAddress(pedHandle);
                if (pedAddress == IntPtr.Zero)
                {
                    return 0;
                }

                IntPtr pedIntelligence = GetCPedIntelligence(pedAddress);
                if (pedIntelligence == IntPtr.Zero)
                {
                    return 0;
                }

                // Actually, the game tests the value at [CTaskInfo + 0x8] using the AND and shr bitwise operations
                // and tests if the value at [CTaskInfo + 0xC] is the task index for CTaskCombat before accessing the member of the target ped pointer
                // In practice, however, it looks like we can use the target address without testing the 2 checks

                var targetPedAddress = new IntPtr(*(long*)(pedIntelligence + PedIntelligenceCombatTargetPedAddressOffset));
                if (targetPedAddress == IntPtr.Zero)
                {
                    return 0;
                }

                return GetEntityHandleFromAddress(targetPedAddress);
            }

            public static void GetScriptTaskHashAndStatus(int pedHandle, out uint taskHash, out uint taskStatus)
            {
                taskHash = 0x811E343C; // the hashed value of SCRIPT_TASK_INVALID, hardcoded in a lot of places
                taskStatus = 3; // the vacant status, hardcoded nearby most of the places where the hashed value of SCRIPT_TASK_INVALID is hardcoded
                if (PedIntelligenceCurrentScriptTaskHashOffset == 0 || PedIntelligenceCurrentScriptTaskStatusOffset == 0)
                {
                    return;
                }

                IntPtr pedAddress = GetEntityAddress(pedHandle);
                if (pedAddress == IntPtr.Zero)
                {
                    return;
                }

                IntPtr pedIntelligence = GetCPedIntelligence(pedAddress);
                if (pedIntelligence == IntPtr.Zero)
                {
                    return;
                }

                taskHash = *(uint*)(pedIntelligence + PedIntelligenceCurrentScriptTaskHashOffset);
                taskStatus = *(uint*)(pedIntelligence + PedIntelligenceCurrentScriptTaskStatusOffset);
            }

            #endregion

            #region -- CPedInventory Data --

            public static IntPtr GetCPedInventoryAddressFromPedHandle(int pedHandle)
            {
                if (IntentoryOfCPedOffset == 0)
                {
                    return IntPtr.Zero;
                }

                IntPtr cPedAddress = GetEntityAddress(pedHandle);
                if (cPedAddress == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }

                return new IntPtr(*(long**)(cPedAddress + IntentoryOfCPedOffset));
            }

            public static uint[] GetAllWeaponHashesOfPedInventory(int pedHandle)
            {
                RageAtArrayPtr* weaponInventoryArray = GetWeaponInventoryArrayOfCPedInventory(pedHandle);
                if (weaponInventoryArray == null)
                {
                    return Array.Empty<uint>();
                }

                ushort weaponInventoryCount = weaponInventoryArray->size;
                var result = new List<uint>(weaponInventoryCount);
                for (int i = 0; i < weaponInventoryCount; i++)
                {
                    ulong itemAddress = weaponInventoryArray->GetElementAddress(i);
                    ItemInfo* weaponInfo = *(ItemInfo**)(itemAddress + 0x8);
                    if (weaponInfo != null)
                    {
                        result.Add(weaponInfo->nameHash);
                    }
                }

                return result.ToArray();
            }

            public static bool TryGetWeaponHashInPedInventoryBySlotHash(int pedHandle, uint slotHash, out uint weaponHash)
            {
                RageAtArrayPtr* weaponInventoryArray = GetWeaponInventoryArrayOfCPedInventory(pedHandle);
                if (weaponInventoryArray == null)
                {
                    weaponHash = 0;
                    return false;
                }

                int arraySize = weaponInventoryArray->size;
                if (arraySize == 0)
                {
                    weaponHash = 0;
                    return false;
                }

                int low = 0, high = arraySize - 1;
                while (true)
                {
                    unsafe
                    {
                        int indexToRead = (low + high) >> 1;
                        ulong currentItem = weaponInventoryArray->GetElementAddress(indexToRead);

                        uint slotHashOfCurrentItem = *(uint*)(currentItem);
                        ItemInfo* weaponInfo = *(ItemInfo**)(currentItem + 0x8);
                        if (slotHashOfCurrentItem == slotHash && weaponInfo != null)
                        {
                            weaponHash = weaponInfo->nameHash;
                            return true;
                        }

                        // The array is sorted in ascending order
                        if (slotHashOfCurrentItem <= slotHash)
                        {
                            low = indexToRead + 1;
                        }
                        else
                        {
                            high = indexToRead - 1;
                        }

                        if (low > high)
                        {
                            weaponHash = 0;
                            return false;
                        }
                    }
                }
            }

            private static RageAtArrayPtr* GetWeaponInventoryArrayOfCPedInventory(int pedHandle)
            {
                if (IntentoryOfCPedOffset == 0)
                {
                    return null;
                }

                IntPtr cPedAddress = GetEntityAddress(pedHandle);
                if (cPedAddress == IntPtr.Zero)
                {
                    return null;
                }

                ulong cPedInventoryAddress = *(ulong*)(cPedAddress + IntentoryOfCPedOffset);
                if (cPedInventoryAddress == 0)
                {
                    return null;
                }

                return (RageAtArrayPtr*)(cPedInventoryAddress + 0x18);
            }

            #endregion
        }

        #region -- Screen Data --

        [StructLayout(LayoutKind.Explicit, Size = 0x30)]
        internal struct ScreenInfo
        {
            // these fields should be in pixel coordinates
            [FieldOffset(0x14)]
            internal uint left;
            [FieldOffset(0x18)]
            internal uint right;
            [FieldOffset(0x1C)]
            internal uint top;
            [FieldOffset(0x20)]
            internal uint bottom;
        }

        private static int* s_physicalScrenWidthAddr;
        private static int* s_physicalScrenHeightAddr;
        private static IntPtr s_screenInfoAddr;
        /// <remarks>
        /// May need to be called in the main thread if the game is using multiple screens.
        /// </remarks>
        private static delegate* unmanaged[Stdcall]<IntPtr, IntPtr> s_unkScreenFunc;
        /// <remarks>
        /// Returns only either 0 or 1.
        /// </remarks>
        private static delegate* unmanaged[Stdcall]<IntPtr, bool> s_isUsingMultiScreenFunc;
        private static delegate* unmanaged[Stdcall]<IntPtr, ScreenInfo*> s_getMainScreenInfoFunc;

        internal sealed class GetMainWindowResoltionTask : IScriptTask
        {
            #region Fields
            internal Size resolutionResult;
            #endregion

            public void Run()
            {
                resolutionResult = new Size(*s_physicalScrenWidthAddr, *s_physicalScrenHeightAddr);

                IntPtr generalScreenInfoAddr = s_unkScreenFunc(s_screenInfoAddr);
                if (s_isUsingMultiScreenFunc(generalScreenInfoAddr))
                {
                    // A lot of functions call this function twice for some reason, so we call it twice for safely
                    generalScreenInfoAddr = s_unkScreenFunc(s_screenInfoAddr);
                    ScreenInfo* screenInfoAddr = s_getMainScreenInfoFunc(generalScreenInfoAddr);

                    resolutionResult = new Size(
                        (int)(screenInfoAddr->right - screenInfoAddr->left),
                        (int)(screenInfoAddr->bottom - screenInfoAddr->top)
                        );
                }
            }
        }

        public static Size GetMainWindowResolution()
        {
            var task = new GetMainWindowResoltionTask();
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);
            return task.resolutionResult;
        }

        #endregion

        #region -- Model Info --

        [StructLayout(LayoutKind.Sequential)]
        private struct HashNode
        {
            internal int hash;
            internal ushort data;
            internal ushort padding;
            internal HashNode* next;
        }

        private enum ModelInfoClassType
        {
            Invalid = 0,
            Object = 1,
            Mlo = 2,
            Time = 3,
            Weapon = 4,
            Vehicle = 5,
            Ped = 6
        }

        private enum VehicleStructClassType
        {
            None = -1,
            Automobile = 0x0,
            Plane = 0x1,
            Trailer = 0x2,
            QuadBike = 0x3,
            SubmarineCar = 0x5,
            AmphibiousAutomobile = 0x6,
            AmphibiousQuadBike = 0x7,
            Heli = 0x8,
            Blimp = 0x9,
            Autogyro = 0xA,
            Bike = 0xB,
            Bicycle = 0xC,
            Boat = 0xD,
            Train = 0xE,
            Submarine = 0xF
        }
        [Flags]
        public enum VehicleFlag1 : ulong
        {
            Big = 0x2,
            IsVan = 0x20,
            CanStandOnTop = 0x10000000,
            LawEnforcement = 0x80000000,
            EmergencyService = 0x100000000,
            AllowsRappel = 0x8000000000,
            IsElectric = 0x80000000000,
            IsOffroadVehicle = 0x1000000000000,
            IsBus = 0x400000000000000,
        }
        [Flags]
        public enum VehicleFlag2 : ulong
        {
            IsTank = 0x200,
            HasBulletProofGlass = 0x1000,
            HasLowriderHydraulics = 0x80000000000000,
            HasLowriderDonkHydraulics = 0x800000000000000,
        }

        [StructLayout(LayoutKind.Explicit, Size = 0x400)]
        private struct CModelList
        {
            [FieldOffset(0x0)]
            internal fixed uint modelMemberIndices[0x100];
        }

        [StructLayout(LayoutKind.Explicit, Size = 0xB8)]
        private struct PedPersonality
        {
            [FieldOffset(0x7C)]
            internal bool isMale;
            [FieldOffset(0x7D)]
            internal bool isHuman;
            [FieldOffset(0x7F)]
            internal bool isGang;
        }

        private static int s_vehicleMakeNameOffsetInModelInfo;
        private static int s_vehicleTypeOffsetInModelInfo;
        private static int s_handlingIndexOffsetInModelInfo;
        private static int s_pedPersonalityIndexOffsetInModelInfo;
        private static UInt32 s_modelNum1;
        private static UInt64 s_modelNum2;
        private static UInt64 s_modelNum3;
        private static UInt64 s_modelNum4;
        private static UInt64 s_modelHashTable;
        private static UInt16 s_modelHashEntries;
        private static ulong* s_modelInfoArrayPtr;
        private static ulong* s_pedPersonalitiesArrayAddr;

        private static ulong* s_cStreamingAddr;
        private static int s_cStreamingAppropriateVehicleIndicesOffset;
        private static int s_cStreamingAppropriatePedIndicesOffset;

        private static IntPtr FindCModelInfo(int modelHash)
        {
            for (HashNode* cur = ((HashNode**)s_modelHashTable)[(uint)(modelHash) % s_modelHashEntries]; cur != null; cur = cur->next)
            {
                if (cur->hash != modelHash)
                {
                    continue;
                }

                ushort data = cur->data;
                bool bitTest = ((*(int*)(s_modelNum2 + (ulong)(4 * data >> 5))) & (1 << (data & 0x1F))) != 0;
                if (data >= s_modelNum1 || !bitTest)
                {
                    continue;
                }

                ulong addr1 = s_modelNum4 + s_modelNum3 * data;
                if (addr1 == 0)
                {
                    continue;
                }

                long* address = (long*)(*(ulong*)(addr1));
                return new IntPtr(address);
            }

            return IntPtr.Zero;
        }

        private static ModelInfoClassType GetModelInfoClass(IntPtr address)
        {
            if (address != IntPtr.Zero)
            {
                return ((ModelInfoClassType)((*(byte*)((ulong)address.ToInt64() + 157) & 0x1F)));
            }

            return ModelInfoClassType.Invalid;
        }

        private static VehicleStructClassType GetVehicleStructClass(IntPtr modelInfoAddress)
        {
            if (GetModelInfoClass(modelInfoAddress) != ModelInfoClassType.Vehicle)
            {
                return VehicleStructClassType.None;
            }

            int typeInt = (*(int*)((byte*)modelInfoAddress.ToPointer() + s_vehicleTypeOffsetInModelInfo));

            // Normalize the value to vehicle type range for b944 or later versions if current game version is earlier than b944.
            // The values for CAmphibiousAutomobile and CAmphibiousQuadBike were inserted between those for CSubmarineCar and CHeli in b944.
            if (s_isEnhanced)
            {
                return (VehicleStructClassType)typeInt;
            }
            if (GetGameVersion() < 28 && typeInt >= 6)
            {
                typeInt += 2;
            }

            return (VehicleStructClassType)typeInt;

        }
        public static int GetVehicleType(int modelHash)
        {
            IntPtr modelInfo = FindCModelInfo(modelHash);

            if (modelInfo == IntPtr.Zero)
            {
                return -1;
            }

            return (int)GetVehicleStructClass(modelInfo);
        }
        // Unchanged in Enhanced.
        private static IntPtr GetModelInfo(IntPtr entityAddress)
        {
            if (entityAddress != IntPtr.Zero)
            {
                return new IntPtr(*(long*)((ulong)entityAddress.ToInt64() + 0x20));
            }

            return IntPtr.Zero;
        }
        // Unchanged in Enhanced.
        private static int GetModelHashFromFwArcheType(IntPtr fwArcheTypeAddress)
        {
            if (fwArcheTypeAddress != IntPtr.Zero)
            {
                return (*(int*)((ulong)fwArcheTypeAddress.ToInt64() + 0x18));
            }

            return 0;
        }
        public static int GetModelHashFromEntity(IntPtr entityAddress)
        {
            if (entityAddress == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr modelInfoAddress = GetModelInfo(entityAddress);
            if (modelInfoAddress != IntPtr.Zero)
            {
                return GetModelHashFromFwArcheType(modelInfoAddress);
            }

            return 0;
        }

        private static bool IsFwArcheTypeAFragment(IntPtr fwArcheTypeAddress)
        {
            if (fwArcheTypeAddress != IntPtr.Zero)
            {
                // The game can't draw fragment entities properly if this value is not 1
                return (*(byte*)((ulong)fwArcheTypeAddress.ToInt64() + 0x60) == 1);
            }

            return false;
        }
        public static bool IsModelAFragment(int modelHash)
        {
            IntPtr modelInfo = FindCModelInfo(modelHash);
            if (modelInfo == IntPtr.Zero)
            {
                return false;
            }

            return IsFwArcheTypeAFragment(modelInfo);
        }

        private static IntPtr GetModelInfoByIndex(uint index)
        {
            if (s_modelInfoArrayPtr == null || index < 0)
            {
                return IntPtr.Zero;
            }

            ulong modelInfoArrayFirstElemPtr = *s_modelInfoArrayPtr;

            return new IntPtr(*(long*)(modelInfoArrayFirstElemPtr + index * 0x8));
        }
        public static List<int> GetLoadedAppropriateVehicleHashes()
        {
            return GetLoadedHashesOfModelList(s_cStreamingAppropriateVehicleIndicesOffset);
        }
        public static List<int> GetLoadedAppropriatePedHashes()
        {
            return GetLoadedHashesOfModelList(s_cStreamingAppropriatePedIndicesOffset);
        }
        internal static List<int> GetLoadedHashesOfModelList(int startOffsetOfCStreaming)
        {
            if (s_modelInfoArrayPtr == null || s_cStreamingAddr == null)
            {
                return new List<int>();
            }

            var resultList = new List<int>();

            const int maxModelListElementCount = 256;
            var modelSet = (CModelList*)((ulong)s_cStreamingAddr + (uint)startOffsetOfCStreaming);
            for (uint i = 0; i < maxModelListElementCount; i++)
            {
                uint indexOfModelInfo = modelSet->modelMemberIndices[i];

                if (indexOfModelInfo == 0xFFFF)
                {
                    break;
                }

                resultList.Add(GetModelHashFromFwArcheType(GetModelInfoByIndex(indexOfModelInfo)));
            }

            return resultList;
        }


        public static bool IsModelAPed(int modelHash)
        {
            IntPtr modelInfo = FindCModelInfo(modelHash);
            return GetModelInfoClass(modelInfo) == ModelInfoClassType.Ped;
        }
        public static bool IsModelABlimp(int modelHash)
        {
            IntPtr modelInfo = FindCModelInfo(modelHash);
            return GetVehicleStructClass(modelInfo) == VehicleStructClassType.Blimp;
        }
        public static bool IsModelAMotorcycle(int modelHash)
        {
            IntPtr modelInfo = FindCModelInfo(modelHash);
            return GetVehicleStructClass(modelInfo) == VehicleStructClassType.Bike;
        }
        public static bool IsModelASubmarine(int modelHash)
        {
            IntPtr modelInfo = FindCModelInfo(modelHash);
            return GetVehicleStructClass(modelInfo) == VehicleStructClassType.Submarine;
        }
        public static bool IsModelASubmarineCar(int modelHash)
        {
            IntPtr modelInfo = FindCModelInfo(modelHash);
            return GetVehicleStructClass(modelInfo) == VehicleStructClassType.SubmarineCar;
        }
        public static bool IsModelATrailer(int modelHash)
        {
            IntPtr modelInfo = FindCModelInfo(modelHash);
            return GetVehicleStructClass(modelInfo) == VehicleStructClassType.Trailer;
        }
        public static bool IsModelAMlo(int modelHash)
        {
            IntPtr modelInfo = FindCModelInfo(modelHash);
            return GetModelInfoClass(modelInfo) == ModelInfoClassType.Mlo;
        }

        public static string GetVehicleMakeName(int modelHash)
        {
            IntPtr modelInfo = FindCModelInfo(modelHash);

            if (GetModelInfoClass(modelInfo) == ModelInfoClassType.Vehicle)
            {
                return StringMarshal.PtrToStringUtf8(modelInfo + s_vehicleMakeNameOffsetInModelInfo);
            }

            return "CARNOTFOUND";
        }

        public static bool HasVehicleFlag(int modelHash, VehicleFlag1 flag) => HasVehicleFlagInternal(modelHash, (ulong)flag, 0x0);
        public static bool HasVehicleFlag(int modelHash, VehicleFlag2 flag) => HasVehicleFlagInternal(modelHash, (ulong)flag, 0x8);
        private static bool HasVehicleFlagInternal(int modelHash, ulong flag, int flagOffset)
        {
            if (Vehicle.FirstVehicleFlagsOffset == 0)
            {
                return false;
            }

            IntPtr modelInfo = FindCModelInfo(modelHash);

            if (GetModelInfoClass(modelInfo) != ModelInfoClassType.Vehicle)
            {
                return false;
            }

            ulong modelFlags = *(ulong*)(modelInfo + Vehicle.FirstVehicleFlagsOffset + flagOffset).ToPointer();
            return (modelFlags & flag) != 0;
        }

        public static ReadOnlyCollection<int> WeaponModels { get; }
        public static ReadOnlyCollection<ReadOnlyCollection<int>> VehicleModels { get; }
        public static ReadOnlyCollection<ReadOnlyCollection<int>> VehicleModelsGroupedByType { get; }
        public static ReadOnlyCollection<int> PedModels { get; }


        private static delegate* unmanaged[Stdcall]<IntPtr, ulong> s_getHandlingDataByHash;
        private static delegate* unmanaged[Stdcall]<int, ulong> s_getHandlingDataByIndex;
        private static delegate* unmanaged[Stdcall]<IntPtr, bool, int> s_getHandlingDataIndexByHash;
        private static ulong s_gHandlingInfoBase;

        public static IntPtr GetHandlingDataByModelHash(int modelHash)
        {
            IntPtr modelInfo = FindCModelInfo(modelHash);
            if (GetModelInfoClass(modelInfo) != ModelInfoClassType.Vehicle)
            {
                return IntPtr.Zero;
            }

            int handlingIndex = *(int*)(modelInfo + s_handlingIndexOffsetInModelInfo).ToPointer();
            return new IntPtr((long)s_getHandlingDataByIndex(handlingIndex));
        }
        public static IntPtr GetHandlingDataByHandlingNameHash(int handlingNameHash)
        {
            if (s_isEnhanced)
            {
                // There is no standalone getHandlingDataByHash function in Enhanced, hence why must implement it ourselves.

                var index = s_getHandlingDataIndexByHash(new IntPtr(&handlingNameHash), true); // This function always returns 0 if false is passed.
                if (index < 0)
                {
                    return IntPtr.Zero;
                }
                return new IntPtr((long)*(ulong*)(s_gHandlingInfoBase + (ulong)(index * 8)));
            }
            return new IntPtr((long)s_getHandlingDataByHash(new IntPtr(&handlingNameHash)));
        }

        private static PedPersonality* GetPedPersonalityElementAddress(IntPtr modelInfoAddress)
        {
            if (modelInfoAddress == IntPtr.Zero ||
                s_pedPersonalitiesArrayAddr == null ||
                s_pedPersonalityIndexOffsetInModelInfo == 0 ||
                *(ulong*)s_pedPersonalitiesArrayAddr == 0)
            {
                return null;
            }

            if (GetModelInfoClass(modelInfoAddress) != ModelInfoClassType.Ped)
            {
                return null;
            }

            // This values is not likely to be changed in further updates
            const int pedPersonalityElementSize = 0xB8; // TODO: Test this.

            ushort indexOfPedPersonality = *(ushort*)(modelInfoAddress + s_pedPersonalityIndexOffsetInModelInfo).ToPointer();
            return (PedPersonality*)(*(ulong*)s_pedPersonalitiesArrayAddr + (uint)(indexOfPedPersonality * pedPersonalityElementSize));
        }
        public static bool IsModelAMalePed(int modelHash)
        {
            PedPersonality* pedPersonalityAddress = GetPedPersonalityElementAddress(FindCModelInfo(modelHash));

            if (pedPersonalityAddress == null)
            {
                return false;
            }

            return pedPersonalityAddress->isMale;
        }
        public static bool IsModelAFemalePed(int modelHash)
        {
            PedPersonality* pedPersonalityAddress = GetPedPersonalityElementAddress(FindCModelInfo(modelHash));

            if (pedPersonalityAddress == null)
            {
                return false;
            }

            return !pedPersonalityAddress->isMale;
        }
        public static bool IsModelHumanPed(int modelHash)
        {
            PedPersonality* pedPersonalityAddress = GetPedPersonalityElementAddress(FindCModelInfo(modelHash));

            if (pedPersonalityAddress == null)
            {
                return false;
            }

            return pedPersonalityAddress->isHuman;
        }
        public static bool IsModelAnAnimalPed(int modelHash)
        {
            PedPersonality* pedPersonalityAddress = GetPedPersonalityElementAddress(FindCModelInfo(modelHash));

            if (pedPersonalityAddress == null)
            {
                return false;
            }

            return !pedPersonalityAddress->isHuman;
        }
        public static bool IsModelAGangPed(int modelHash)
        {
            PedPersonality* pedPersonalityAddress = GetPedPersonalityElementAddress(FindCModelInfo(modelHash));

            if (pedPersonalityAddress == null)
            {
                return false;
            }

            return pedPersonalityAddress->isGang;
        }

        #endregion

        #region -- Entity Pools --

        // Note: actually this struct is supposed to point the same struct type as `FwBasePool` in this source code
        // file, but needs to be careful when refactoring.
        [StructLayout(LayoutKind.Explicit)]
        private struct FwScriptGuidPoolLegacy
        {
            // The max count value should be at least 3072 as long as ScriptHookV is installed.
            // Without ScriptHookV, the default value is hardcoded and may be different between different game versions (the value is 300 in b372 and 700 in b2824).
            // The default value (when running without ScriptHookV) can be found by searching the dumped exe or the game memory with "D7 A8 11 73" (0x7311A8D7).
            [FieldOffset(0x10)]
            internal uint maxCount;
            [FieldOffset(0x14)]
            internal int itemSize;
            [FieldOffset(0x18)]
            internal int firstEmptySlot;
            [FieldOffset(0x1C)]
            internal int emptySlots;
            [FieldOffset(0x20)]
            internal uint itemCount;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal bool IsFull()
            {
                return maxCount - (itemCount & 0x3FFFFFFF) <= 256;
            }
        }

        // All the fields shifted by 0x08 in Enhanced.
        [StructLayout(LayoutKind.Explicit)]
        private struct FwScriptGuidPoolEnhanced
        {
            [FieldOffset(0x18)]
            internal uint maxCount;
            [FieldOffset(0x1c)]
            internal int itemSize;
            [FieldOffset(0x20)]
            internal int firstEmptySlot;
            [FieldOffset(0x24)]
            internal int emptySlots;
            [FieldOffset(0x28)]
            internal uint itemCount;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal bool IsFull()
            {
                return maxCount - (itemCount & 0x3FFFFFFF) <= 256;
            }
        }
        /// <summary>
        /// Represents <c>rage::sysMemPoolAllocator</c>, which all of the
        /// <c>rage::sysMemPoolAllocator::PoolWrapper&lt;typename T&gt;</c> have as the sole field via an pointer.
        /// </summary>
        /// <remarks>
        /// Possible (without limitation) <c>typename T</c>s of
        /// <c>rage::sysMemPoolAllocator::PoolWrapper&lt;typename T&gt;</c> are <c>CTask</c>, <c>CTaskInfo</c>,
        /// <c>CVehicle</c>, <c>audVehicleAudioEntity</c>, and <c>void *</c>.
        /// </remarks>
        [StructLayout(LayoutKind.Explicit)]
        private struct RageSysMemPoolAllocatorLegacy
        {
            // m_pool at offset 0x0 takes 0x60 byte
            // (type: "rage::atIteratablePool<rage::sysMemPoolAllocator::PoolNode>").
            [FieldOffset(0x00)]
            internal ulong* poolAddress;
            [FieldOffset(0x08)]
            internal uint size;
            [FieldOffset(0x30)]
            internal uint* bitArray;

            // m_freeList at 0x60 takes 0x18 bytes (type: "rage::inlist<rage::sysMemPoolAllocator::FreeNode,8>").
            // The struct contains m_head, m_tail and m_size fields.
            [FieldOffset(0x60)]
            internal uint itemCount;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal bool IsValid(uint i)
            {
                return ((bitArray[i >> 5] >> ((int)i & 0x1F)) & 1) != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal ulong GetAddress(uint i)
            {
                return poolAddress[i];
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct RageSysMemPoolAllocatorEnhanced
        {
            [FieldOffset(0x08)]
            internal ulong* poolAddress;
            [FieldOffset(0x10)]
            internal uint size;
            [FieldOffset(0x38)]
            internal uint* bitArray;
            [FieldOffset(0x68)]
            internal uint itemCount;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal bool IsValid(uint i)
            {
                return ((bitArray[i >> 5] >> ((int)i & 0x1F)) & 1) != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal ulong GetAddress(uint i)
            {
                return poolAddress[i];
            }
        }

        /// <summary>
        /// Represents <c>rage::fwBasePool</c>, which all of the <c>rage::fwPool&lt;typename T&gt;</c> types has as
        /// the sole field.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The <c>rage::fwBasePool</c> takes 0x30 bytes without a vtable pointer in production builds, but that in
        /// a debug build around v1.0.2699.0 takes 0x68 bytes with a vtable pointer and additional debug fields.
        /// </para>
        /// <para>
        /// All of <c>rage::fwPool&lt;typename T&gt;</c> types (at least 243 types) has the same layout but with
        /// different element type (at least the return type of <c>GetSlot(int)</c> differs by type parameter).
        /// </para>
        /// </remarks>
        [StructLayout(LayoutKind.Explicit)]
        private struct FwBasePoolLegacy
        {
            [FieldOffset(0x00)]
            public ulong poolStartAddress;
            [FieldOffset(0x08)]
            public IntPtr byteArray;
            [FieldOffset(0x10)]
            public uint size;
            [FieldOffset(0x14)]
            public uint itemSize;

            // The "first" index should be at 0x18 and The "last" index should be at 0x1C in production builds
            // according to the layout in a debug build around v1.0.2699.0, but the "first" and the "last" aren't
            // related to about the order.

            // WARNING: according to `rage::fwBasePoolTracker::GetNoOfUsedSpaces`, this field is supposed to be read
            // by reading as a 4-byte value, applying left shift by 2 and SIGNED right shift (`SAR` in assembly code)
            // by 2, and then return the calculated value.
            [FieldOffset(0x20)]
            public ushort itemCount;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool IsValid(uint index)
            {
                return Mask(index) != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool IsHandleValid(int handle)
            {
                uint handleUInt = (uint)handle;
                uint index = handleUInt >> 8;
                return GetCounter(index) == (handleUInt & 0xFFu);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ulong GetAddress(uint index)
            {
                return ((Mask(index) & (poolStartAddress + index * itemSize)));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public IntPtr GetAddressFromHandle(int handle)
            {
                return IsHandleValid(handle) ? new IntPtr((long)GetAddress((uint)handle >> 8)) : IntPtr.Zero;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int GetGuidHandleByIndex(uint index)
            {
                return IsValid(index) ? (int)((index << 8) + GetCounter(index)) : 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int GetGuidHandleFromAddress(ulong address)
            {
                if (address < poolStartAddress || address >= poolStartAddress + size * itemSize)
                {
                    return 0;
                }

                ulong offset = address - poolStartAddress;
                if (offset % itemSize != 0)
                {
                    return 0;
                }

                uint indexOfPool = (uint)(offset / itemSize);
                return (int)((indexOfPool << 8) + GetCounter(indexOfPool));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private byte GetCounter(uint index)
            {
                unsafe
                {
                    byte* byteArrayPtr = (byte*)byteArray.ToPointer();
                    return (byte)(byteArrayPtr[index] & 0x7F);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private ulong Mask(uint index)
            {
                unsafe
                {
                    byte* byteArrayPtr = (byte*)byteArray.ToPointer();
                    long num1 = byteArrayPtr[index] & 0x80;
                    return (ulong)(~((num1 | -num1) >> 63));
                }
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FwBasePoolEnhanced
        {
            [FieldOffset(0x08)]
            public ulong poolStartAddress;
            [FieldOffset(0x10)]
            public IntPtr byteArray;
            [FieldOffset(0x18)]
            public uint size;
            [FieldOffset(0x1c)]
            public uint itemSize;
            [FieldOffset(0x28)]
            public ushort itemCount;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool IsValid(uint index)
            {
                return Mask(index) != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool IsHandleValid(int handle)
            {
                uint handleUInt = (uint)handle;
                uint index = handleUInt >> 8;
                return GetCounter(index) == (handleUInt & 0xFFu);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ulong GetAddress(uint index)
            {
                return ((Mask(index) & (poolStartAddress + index * itemSize)));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public IntPtr GetAddressFromHandle(int handle)
            {
                return IsHandleValid(handle) ? new IntPtr((long)GetAddress((uint)handle >> 8)) : IntPtr.Zero;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int GetGuidHandleByIndex(uint index)
            {
                return IsValid(index) ? (int)((index << 8) + GetCounter(index)) : 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int GetGuidHandleFromAddress(ulong address)
            {
                if (address < poolStartAddress || address >= poolStartAddress + size * itemSize)
                {
                    return 0;
                }

                ulong offset = address - poolStartAddress;
                if (offset % itemSize != 0)
                {
                    return 0;
                }

                uint indexOfPool = (uint)(offset / itemSize);
                return (int)((indexOfPool << 8) + GetCounter(indexOfPool));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private byte GetCounter(uint index)
            {
                unsafe
                {
                    byte* byteArrayPtr = (byte*)byteArray.ToPointer();
                    return (byte)(byteArrayPtr[index] & 0x7F);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private ulong Mask(uint index)
            {
                unsafe
                {
                    byte* byteArrayPtr = (byte*)byteArray.ToPointer();
                    long num1 = byteArrayPtr[index] & 0x80;
                    return (ulong)(~((num1 | -num1) >> 63));
                }
            }
        }

        private static ulong* s_fwScriptGuidPoolAddress;
        private static ulong* s_pedPoolAddress;
        private static ulong* s_objectPoolAddress;
        private static ulong* s_pickupObjectPoolAddress;
        private static ulong* s_pickupObjectPlacementPoolAddress;
        private static ulong* s_vehiclePoolAddress;
        private static ulong* s_buildingPoolAddress;
        private static ulong* s_animatedBuildingPoolAddress;
        private static ulong* s_interiorInstPoolAddress;
        private static ulong* s_interiorProxyPoolAddress;

        private static ulong* s_projectilePoolAddress;
        private static int* s_projectileCountAddress;
        private static uint s_pickupObjectPlacementPositionOffset;

        // if the entity is a ped and they are in a vehicle, the vehicle position will be returned instead (just like GET_ENTITY_COORDS does)
        private static delegate* unmanaged[Stdcall]<ulong, float*, ulong> s_entityPosFunc;
        // should be rage::fwScriptGuid::CreateGuid
        private static delegate* unmanaged[Stdcall]<ulong, int> s_createGuid;

        internal sealed class FwScriptGuidPoolTask : IScriptTask
        {
            internal enum PoolType
            {
                Generic,
                Vehicle,
                Projectile,
            }

            #region Fields
            internal PoolType _poolType;
            internal IntPtr _poolAddress;

            internal bool _doPosCheck = false;
            internal bool _doModelCheck = false;
            internal float _radiusSquared;
            internal FVector3? _position;
            internal HashSet<int> _modelHashes;
            internal Func<IntPtr, bool> _predicate;
            internal int[] _resultHandles = Array.Empty<int>();

            #endregion

            internal FwScriptGuidPoolTask(PoolType type, IntPtr poolAddress)
            {
                _poolType = type;
                _poolAddress = poolAddress;
            }
            internal FwScriptGuidPoolTask(PoolType type, IntPtr poolAddress, int[] modelHashes) : this(type, poolAddress)
            {
                if (modelHashes == null || modelHashes.Length <= 0)
                {
                    return;
                }

                _doModelCheck = true;
                this._modelHashes = new HashSet<int>(modelHashes);
            }
            internal FwScriptGuidPoolTask(PoolType type, IntPtr poolAddress, Func<IntPtr, bool> predicate) : this(type, poolAddress)
            {
                _predicate = predicate;
            }
            internal FwScriptGuidPoolTask(PoolType type, IntPtr poolAddress, FVector3 position, float radiusSquared,
                int[] modelHashes = null, Func<IntPtr, bool> predicate = null) : this(type, poolAddress)
            {
                _doPosCheck = true;
                this._radiusSquared = radiusSquared;
                this._position = position;

                _predicate = predicate;

                if (modelHashes == null || modelHashes.Length <= 0)
                {
                    return;
                }

                _doModelCheck = true;
                this._modelHashes = new HashSet<int>(modelHashes);
            }

            public void Run()
            {
                if (s_isEnhanced ? (NativeMemory.s_fwScriptGuidPoolAddress == null) : (*NativeMemory.s_fwScriptGuidPoolAddress == 0))
                {
                    return;
                }

                var fwScriptGuidPool = s_isEnhanced ? (void*)NativeMemory.s_fwScriptGuidPoolAddress : (void*)(*NativeMemory.s_fwScriptGuidPoolAddress);
                switch (_poolType)
                {
                    case PoolType.Generic:
                        var fwBasePool = (void*)_poolAddress;
                        _resultHandles = GetGuidHandlesFromFwBasePool(fwScriptGuidPool, fwBasePool);
                        break;

                    case PoolType.Vehicle:
                        var poolAllocator = (void*)_poolAddress;
                        _resultHandles = GetGuidHandlesFromRageSysMemPoolAllocator(fwScriptGuidPool, poolAllocator);
                        break;

                    case PoolType.Projectile:
                        int projectilesCount = NativeMemory.GetProjectileCount();
                        int projectileCapacity = NativeMemory.GetProjectileCapacity();
                        ulong* projectilePoolAddress = (ulong*)_poolAddress;

                        _resultHandles = GetGuidHandlesFromProjectilePool(fwScriptGuidPool, projectilePoolAddress, projectilesCount, projectileCapacity, _predicate);
                        break;
                }
            }

            private int[] GetGuidHandlesFromFwBasePool(void* fwScriptGuidPool, void* fwBasePool)
            {
                FwScriptGuidPoolEnhanced* enhancedFwScriptGuidPool = (FwScriptGuidPoolEnhanced*)fwScriptGuidPool;
                FwBasePoolEnhanced* enhancedFwBasePool = (FwBasePoolEnhanced*)fwBasePool;
                FwScriptGuidPoolLegacy* legacyFwScriptGuidPool = (FwScriptGuidPoolLegacy*)fwScriptGuidPool;
                FwBasePoolLegacy* legacyFwBasePool = (FwBasePoolLegacy*)fwBasePool;

                var resultList = new List<int>(s_isEnhanced ? enhancedFwBasePool->itemCount : legacyFwBasePool->itemCount);
                uint fwBasePoolSize = s_isEnhanced ? enhancedFwBasePool->size : legacyFwBasePool->size;
                for (uint i = 0; i < fwBasePoolSize; i++)
                {
                    if (s_isEnhanced ? enhancedFwScriptGuidPool->IsFull() : legacyFwScriptGuidPool->IsFull())
                    {
                        throw new InvalidOperationException("The fwScriptGuid pool is full. The pool must be extended to retrieve all entity handles.");
                    }

                    if (s_isEnhanced ? !enhancedFwBasePool->IsValid(i) : !legacyFwBasePool->IsValid(i))
                    {
                        continue;
                    }

                    ulong address = s_isEnhanced ? enhancedFwBasePool->GetAddress(i) : legacyFwBasePool->GetAddress(i);

                    if (_doPosCheck && !CheckEntityDistance(address, _position.GetValueOrDefault(), _radiusSquared))
                    {
                        continue;
                    }

                    if (_doModelCheck && !CheckEntityModel(address, _modelHashes))
                    {
                        continue;
                    }

                    int createdHandle = NativeMemory.s_createGuid(address);
                    resultList.Add(createdHandle);
                }
                return resultList.ToArray();
            }

            private int[] GetGuidHandlesFromRageSysMemPoolAllocator(void* fwScriptGuidPool, void* poolAllocator)
            {
                FwScriptGuidPoolEnhanced* enhancedFwScriptGuidPool = (FwScriptGuidPoolEnhanced*)fwScriptGuidPool;
                RageSysMemPoolAllocatorEnhanced* enhancedPoolAllocator = (RageSysMemPoolAllocatorEnhanced*)poolAllocator;
                FwScriptGuidPoolLegacy* legacyFwScriptGuidPool = (FwScriptGuidPoolLegacy*)fwScriptGuidPool;
                RageSysMemPoolAllocatorLegacy* legacyPoolAllocator = (RageSysMemPoolAllocatorLegacy*)poolAllocator;

                var resultList = new List<int>(s_isEnhanced ? (int)enhancedPoolAllocator->itemCount : (int)legacyPoolAllocator->itemCount);

                uint poolSize = s_isEnhanced ? enhancedPoolAllocator->size : legacyPoolAllocator->size;
                for (uint i = 0; i < poolSize; i++)
                {
                    if (s_isEnhanced ? enhancedFwScriptGuidPool->IsFull() : legacyFwScriptGuidPool->IsFull())
                    {
                        throw new InvalidOperationException("The fwScriptGuid pool is full. The pool must be extended to retrieve all vehicle handles.");
                    }

                    if (s_isEnhanced ? !enhancedPoolAllocator->IsValid(i) : !legacyPoolAllocator->IsValid(i))
                    {
                        continue;
                    }

                    ulong address = s_isEnhanced ? enhancedPoolAllocator->GetAddress(i) : legacyPoolAllocator->GetAddress(i);

                    if (_doPosCheck && !CheckEntityDistance(address, _position.GetValueOrDefault(), _radiusSquared))
                    {
                        continue;
                    }

                    if (_doModelCheck && !CheckEntityModel(address, _modelHashes))
                    {
                        continue;
                    }

                    int createdHandle = NativeMemory.s_createGuid(address);
                    resultList.Add(createdHandle);
                }

                return resultList.ToArray();
            }

            // TODO: Check this.
            private int[] GetGuidHandlesFromProjectilePool(void* fwScriptGuidPool,
                ulong* projectilePool, int itemCount, int maxItemCount, Func<IntPtr, bool> predicate)
            {
                FwScriptGuidPoolEnhanced* enhancedFwScriptGuidPool = (FwScriptGuidPoolEnhanced*)fwScriptGuidPool;
                FwScriptGuidPoolLegacy* legacyFwScriptGuidPool = (FwScriptGuidPoolLegacy*)fwScriptGuidPool;

                int projectilesLeft = itemCount;
                int projectileCapacity = maxItemCount;

                var resultList = new List<int>(itemCount);

                for (uint i = 0; (projectilesLeft > 0 && i < projectileCapacity); i++)
                {
                    if (s_isEnhanced ? enhancedFwScriptGuidPool->IsFull() : legacyFwScriptGuidPool->IsFull())
                    {
                        throw new InvalidOperationException("The fwScriptGuid pool is full. The pool must be extended to retrieve all projectile handles.");
                    }

                    ulong entityAddress = (ulong)MemDataMarshal.ReadAddress(new IntPtr(projectilePool + i)).ToInt64();
                    if (entityAddress == 0)
                    {
                        continue;
                    }

                    projectilesLeft--;

                    if (_doPosCheck && !CheckEntityDistance(entityAddress, _position.GetValueOrDefault(), _radiusSquared))
                    {
                        continue;
                    }

                    if (_doModelCheck && !CheckEntityModel(entityAddress, _modelHashes))
                    {
                        continue;
                    }

                    if (predicate != null && !predicate((IntPtr)entityAddress))
                    {
                        continue;
                    }

                    int createdHandle = NativeMemory.s_createGuid(entityAddress);
                    resultList.Add(createdHandle);
                }

                return resultList.ToArray();
            }

            private static bool CheckEntityDistance(ulong address, FVector3 position, float radiusSquared)
            {
                float* entityPosition = stackalloc float[4];

                GetEntityPos(address, entityPosition);
                float x = position.X - entityPosition[0];
                float y = position.Y - entityPosition[1];
                float z = position.Z - entityPosition[2];

                float distanceSquared = (x * x) + (y * y) + (z * z);
                if (distanceSquared > radiusSquared)
                {
                    return false;
                }

                return true;
            }

            private static bool CheckEntityModel(ulong address, HashSet<int> modelHashes)
            {
                int modelHash = GetModelHashFromEntity(new IntPtr((long)address));
                if (!modelHashes.Contains(modelHash))
                {
                    return false;
                }

                return true;
            }
        }

        internal sealed class GetEntityHandleTask : IScriptTask
        {
            #region Fields
            internal ulong _entityAddress;
            internal int _returnEntityHandle;
            #endregion

            internal GetEntityHandleTask(IntPtr entityAddress)
            {
                this._entityAddress = (ulong)entityAddress.ToInt64();
            }

            public void Run()
            {
                _returnEntityHandle = NativeMemory.s_createGuid(_entityAddress);
            }
        }

        public static int GetVehicleCount()
        {
            if (*s_vehiclePoolAddress == 0)
            {
                return 0;
            }

            if (s_isEnhanced)
            {
                RageSysMemPoolAllocatorEnhanced* pool = *(RageSysMemPoolAllocatorEnhanced**)(*s_vehiclePoolAddress);
                return (int)pool->itemCount;
            }
            else
            {
                RageSysMemPoolAllocatorLegacy* pool = *(RageSysMemPoolAllocatorLegacy**)(*s_vehiclePoolAddress);
                return (int)pool->itemCount;
            }
        }

        public static int GetPedCount() => s_pedPoolAddress != null ? GetFwBasePoolCount(s_pedPoolAddress) : 0;
        public static int GetObjectCount() => s_objectPoolAddress != null ? GetFwBasePoolCount(s_objectPoolAddress) : 0;
        public static int GetPickupObjectCount() => s_pickupObjectPoolAddress != null ? GetFwBasePoolCount(s_pickupObjectPoolAddress) : 0;
        public static int GetPickupObjectPlacementCount() => s_pickupObjectPlacementPoolAddress != null ? GetFwBasePoolCount(s_pickupObjectPlacementPoolAddress) : 0;
        public static int GetBuildingCount() => s_buildingPoolAddress != null ? GetFwBasePoolCount(s_buildingPoolAddress) : 0;
        public static int GetAnimatedBuildingCount() => s_animatedBuildingPoolAddress != null ? GetFwBasePoolCount(s_animatedBuildingPoolAddress) : 0;
        public static int GetInteriorInstCount() => s_interiorInstPoolAddress != null ? GetFwBasePoolCount(s_interiorInstPoolAddress) : 0;
        public static int GetInteriorProxyCount() => s_interiorProxyPoolAddress != null ? GetFwBasePoolCount(s_interiorProxyPoolAddress) : 0;
        public static int GetProjectileCount() => s_projectileCountAddress != null ? *s_projectileCountAddress : 0;

        private static int GetFwBasePoolCount(ulong* address)
        {
            if (s_isEnhanced)
            {
                var pool = (FwBasePoolEnhanced*)(address);
                return (int)pool->itemCount;
            }
            else
            {
                var pool = (FwBasePoolLegacy*)(*address);
                return (int)pool->itemCount;
            }
        }

        public static int GetVehicleCapacity()
        {
            if (*s_vehiclePoolAddress == 0)
            {
                return 0;
            }

            if (s_isEnhanced)
            {
                RageSysMemPoolAllocatorEnhanced* pool = *(RageSysMemPoolAllocatorEnhanced**)(*s_vehiclePoolAddress);
                return (int)pool->size;
            }
            else
            {
                RageSysMemPoolAllocatorLegacy* pool = *(RageSysMemPoolAllocatorLegacy**)(*s_vehiclePoolAddress);
                return (int)pool->size;
            }
        }
        public static int GetPedCapacity() => s_pedPoolAddress != null ? GetFwBasePoolCapacity(s_pedPoolAddress) : 0;
        public static int GetObjectCapacity() => s_objectPoolAddress != null ? GetFwBasePoolCapacity(s_objectPoolAddress) : 0;
        public static int GetPickupObjectCapacity() => s_pickupObjectPoolAddress != null ? GetFwBasePoolCapacity(s_pickupObjectPoolAddress) : 0;
        public static int GetPickupObjectPlacementCapacity() => s_pickupObjectPlacementPoolAddress != null ? GetFwBasePoolCapacity(s_pickupObjectPlacementPoolAddress) : 0;
        public static int GetBuildingCapacity() => s_buildingPoolAddress != null ? GetFwBasePoolCapacity(s_buildingPoolAddress) : 0;
        public static int GetAnimatedBuildingCapacity() => s_animatedBuildingPoolAddress != null ? GetFwBasePoolCapacity(s_animatedBuildingPoolAddress) : 0;
        public static int GetInteriorInstCapacity() => s_interiorInstPoolAddress != null ? GetFwBasePoolCapacity(s_interiorInstPoolAddress) : 0;
        public static int GetInteriorProxyCapacity() => s_interiorProxyPoolAddress != null ? GetFwBasePoolCapacity(s_interiorProxyPoolAddress) : 0;
        // the max number of projectile has not been changed from 50
        public static int GetProjectileCapacity() => 50;

        private static int GetFwBasePoolCapacity(ulong* address)
        {
            if (s_isEnhanced)
            {
                var pool = (FwBasePoolEnhanced*)(address);
                return (int)pool->size;
            }
            else
            {
                var pool = (FwBasePoolLegacy*)(*address);
                return (int)pool->size;
            }
        }

        public static int[] GetPedHandles(int[] modelHashes = null)
        {
            return GetGuidsInFwBasePool(NativeMemory.s_pedPoolAddress, modelHashes);
        }
        public static int[] GetPedHandles(FVector3 position, float radius, int[] modelHashes = null)
        {
            return GetGuidsInFwBasePool(NativeMemory.s_pedPoolAddress, position, radius, modelHashes);
        }

        public static int[] GetPropHandles(int[] modelHashes = null)
        {
            return GetGuidsInFwBasePool(NativeMemory.s_objectPoolAddress, modelHashes);
        }
        public static int[] GetPropHandles(FVector3 position, float radius, int[] modelHashes = null)
        {
            return GetGuidsInFwBasePool(NativeMemory.s_objectPoolAddress, position, radius, modelHashes);
        }

        public static int[] GetEntityHandles()
        {
            int[] vehicleHandles = GetVehicleHandles();
            int[] pedHandles = GetPedHandles();
            int[] propHandles = GetPropHandles();

            return BuildOneArrayFromElementsOfEntityHandleArrays(vehicleHandles, pedHandles, propHandles);
        }
        public static int[] GetEntityHandles(FVector3 position, float radius)
        {
            int[] vehicleHandles = GetVehicleHandles(position, radius);
            int[] pedHandles = GetPedHandles(position, radius);
            int[] propHandles = GetPropHandles(position, radius);

            return BuildOneArrayFromElementsOfEntityHandleArrays(vehicleHandles, pedHandles, propHandles);
        }

        private static int[] BuildOneArrayFromElementsOfEntityHandleArrays(int[] vehicleHandles, int[] pedHandles, int[] propHandles)
        {
            int entityHandleCount = vehicleHandles.Length + pedHandles.Length + propHandles.Length;
            int[] entityHandles = new int[entityHandleCount];

            Array.Copy(vehicleHandles, 0, entityHandles, 0, vehicleHandles.Length);
            Array.Copy(pedHandles, 0, entityHandles, vehicleHandles.Length, pedHandles.Length);
            Array.Copy(propHandles, 0, entityHandles, vehicleHandles.Length + pedHandles.Length, propHandles.Length);

            return entityHandles;
        }

        public static int[] GetVehicleHandles(int[] modelHashes = null)
        {
            if (*NativeMemory.s_vehiclePoolAddress == 0)
            {
                return Array.Empty<int>();
            }

            IntPtr poolAllocator;
            if (s_isEnhanced)
            {
                poolAllocator = new IntPtr(*(RageSysMemPoolAllocatorEnhanced**)(*NativeMemory.s_vehiclePoolAddress));
            }
            else
            {
                poolAllocator = new IntPtr(*(RageSysMemPoolAllocatorLegacy**)(*NativeMemory.s_vehiclePoolAddress));
            }
            
            var task = new FwScriptGuidPoolTask(FwScriptGuidPoolTask.PoolType.Vehicle, poolAllocator, modelHashes);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._resultHandles;
        }
        public static int[] GetVehicleHandles(FVector3 position, float radius, int[] modelHashes = null)
        {
            if (*NativeMemory.s_vehiclePoolAddress == 0)
            {
                return Array.Empty<int>();
            }

            IntPtr poolAllocator;
            if (s_isEnhanced)
            {
                poolAllocator = new IntPtr(*(RageSysMemPoolAllocatorEnhanced**)(*NativeMemory.s_vehiclePoolAddress));
            }
            else
            {
                poolAllocator = new IntPtr(*(RageSysMemPoolAllocatorLegacy**)(*NativeMemory.s_vehiclePoolAddress));
            }
            
            var task = new FwScriptGuidPoolTask(FwScriptGuidPoolTask.PoolType.Vehicle, poolAllocator, position, radius * radius, modelHashes);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._resultHandles;
        }

        public static int[] GetPickupObjectHandles()
        {
            return GetGuidsInFwBasePool(NativeMemory.s_pickupObjectPoolAddress);
        }
        public static int[] GetPickupObjectHandles(FVector3 position, float radius)
        {
            return GetGuidsInFwBasePool(NativeMemory.s_pickupObjectPoolAddress, position, radius);
        }

        public static ulong[] GetPickupObjectPlacementAddresses()
        {
            if (s_pickupObjectPlacementPoolAddress == null)
            {
                return Array.Empty<ulong>();
            }
            return GetAddressesInFwBasePool(NativeMemory.s_pickupObjectPlacementPoolAddress);
        }
        public static ulong[] GetPickupObjectPlacementAddresses(FVector3 position, float radius)
        {
            if (s_pickupObjectPlacementPoolAddress == null)
            {
                return Array.Empty<ulong>();
            }
            return GetCEntityAddressesInRange(NativeMemory.s_pickupObjectPlacementPoolAddress, position, radius);
        }

        public static int[] GetProjectileHandles()
        {
            if (NativeMemory.s_projectilePoolAddress == null)
            {
                return Array.Empty<int>();
            }

            var task = new FwScriptGuidPoolTask(FwScriptGuidPoolTask.PoolType.Projectile, new IntPtr(NativeMemory.s_projectilePoolAddress));
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._resultHandles;
        }
        public static int[] GetProjectileHandles(FVector3 position, float radius)
        {
            if (NativeMemory.s_projectilePoolAddress == null)
            {
                return Array.Empty<int>();
            }

            var task = new FwScriptGuidPoolTask(FwScriptGuidPoolTask.PoolType.Projectile,
                new IntPtr(NativeMemory.s_projectilePoolAddress), position, radius * radius);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._resultHandles;
        }
        public static int[] GetRocketProjectileHandles()
        {
            if (NativeMemory.s_projectilePoolAddress == null)
            {
                return Array.Empty<int>();
            }

            var task = new FwScriptGuidPoolTask(FwScriptGuidPoolTask.PoolType.Projectile,
                new IntPtr(NativeMemory.s_projectilePoolAddress),
                address => GetAsCProjectileRocket(address) != IntPtr.Zero);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._resultHandles;
        }
        public static int[] GetRocketProjectileHandles(FVector3 position, float radius)
        {
            if (NativeMemory.s_projectilePoolAddress == null)
            {
                return Array.Empty<int>();
            }

            var task = new FwScriptGuidPoolTask(FwScriptGuidPoolTask.PoolType.Projectile,
                new IntPtr(NativeMemory.s_projectilePoolAddress), position, radius * radius,
                predicate: address => GetAsCProjectileRocket(address) != IntPtr.Zero);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._resultHandles;
        }
        public static int[] GetThrownProjectileHandles()
        {
            if (NativeMemory.s_projectilePoolAddress == null)
            {
                return Array.Empty<int>();
            }

            var task = new FwScriptGuidPoolTask(FwScriptGuidPoolTask.PoolType.Projectile,
                new IntPtr(NativeMemory.s_projectilePoolAddress),
                address => GetAsCProjectileThrown(address) != IntPtr.Zero);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._resultHandles;
        }
        public static int[] GetThrownProjectileHandles(FVector3 position, float radius)
        {
            if (NativeMemory.s_projectilePoolAddress == null)
            {
                return Array.Empty<int>();
            }

            var task = new FwScriptGuidPoolTask(FwScriptGuidPoolTask.PoolType.Projectile,
                new IntPtr(NativeMemory.s_projectilePoolAddress), position, radius * radius,
                predicate: address => GetAsCProjectileThrown(address) != IntPtr.Zero);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._resultHandles;
        }

        private static int[] GetGuidsInFwBasePool(ulong* ptrOfPoolPtr)
        {     
            var fwBasePool = new IntPtr(s_isEnhanced ? (FwBasePoolEnhanced*)(ptrOfPoolPtr) : (FwBasePoolLegacy*)(*ptrOfPoolPtr));

            if (fwBasePool == IntPtr.Zero)
            {
                return Array.Empty<int>();
            }

            var task = new FwScriptGuidPoolTask(FwScriptGuidPoolTask.PoolType.Generic, fwBasePool);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._resultHandles;
        }
        private static int[] GetGuidsInFwBasePool(ulong* ptrOfPoolPtr, int[] modelHashes)
        {
            var fwBasePool = new IntPtr(s_isEnhanced ? (FwBasePoolEnhanced*)(ptrOfPoolPtr) : (FwBasePoolLegacy*)(*ptrOfPoolPtr));

            if (fwBasePool == IntPtr.Zero)
            {
                return Array.Empty<int>();
            }

            var task = new FwScriptGuidPoolTask(FwScriptGuidPoolTask.PoolType.Generic, fwBasePool, modelHashes);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._resultHandles;
        }

        private static int[] GetGuidsInFwBasePool(ulong* ptrOfPoolPtr, FVector3 position, float radius, int[] modelHashes = null)
        {
            var fwBasePool = new IntPtr(s_isEnhanced ? (FwBasePoolEnhanced*)(ptrOfPoolPtr) : (FwBasePoolLegacy*)(*ptrOfPoolPtr));

            if (fwBasePool == IntPtr.Zero)
            {
                return Array.Empty<int>();
            }

            var task = new FwScriptGuidPoolTask(FwScriptGuidPoolTask.PoolType.Generic, fwBasePool, position, radius * radius, modelHashes);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._resultHandles;
        }

        public static int[] GetBuildingHandles()
        {
            if (s_buildingPoolAddress == null)
            {
                return Array.Empty<int>();
            }

            return GetHandlesInFwBasePool(NativeMemory.s_buildingPoolAddress);
        }

        public static int[] GetBuildingHandles(FVector3 position, float radius)
        {
            if (s_buildingPoolAddress == null)
            {
                return Array.Empty<int>();
            }

            return GetCEntityHandlesInRange(NativeMemory.s_buildingPoolAddress, position, radius);
        }

        public static int[] GetAnimatedBuildingHandles()
        {
            if (s_animatedBuildingPoolAddress == null)
            {
                return Array.Empty<int>();
            }

            return GetHandlesInFwBasePool(NativeMemory.s_animatedBuildingPoolAddress);
        }

        public static int[] GetAnimatedBuildingHandles(FVector3 position, float radius)
        {
            if (s_animatedBuildingPoolAddress == null)
            {
                return Array.Empty<int>();
            }

            return GetCEntityHandlesInRange(NativeMemory.s_animatedBuildingPoolAddress, position, radius);
        }

        public static int[] GetInteriorInstHandles()
        {
            if (s_interiorInstPoolAddress == null)
            {
                return Array.Empty<int>();
            }

            return GetHandlesInFwBasePool(NativeMemory.s_interiorInstPoolAddress);
        }

        public static int[] GetInteriorInstHandles(FVector3 position, float radius)
        {
            if (s_interiorInstPoolAddress == null)
            {
                return Array.Empty<int>();
            }

            return GetCEntityHandlesInRange(NativeMemory.s_interiorInstPoolAddress, position, radius);
        }

        public static int[] GetInteriorProxyHandles()
        {
            if (s_interiorProxyPoolAddress == null)
            {
                return Array.Empty<int>();
            }

            return GetHandlesInFwBasePool(NativeMemory.s_interiorProxyPoolAddress);
        }

        public static int[] GetInteriorProxyHandles(FVector3 position, float radius)
        {
            if (s_interiorProxyPoolAddress == null)
            {
                return Array.Empty<int>();
            }
            FwBasePoolEnhanced* enhancedPool = (FwBasePoolEnhanced*)(NativeMemory.s_interiorProxyPoolAddress);
            FwBasePoolLegacy* legacyPool = (FwBasePoolLegacy*)(*NativeMemory.s_interiorProxyPoolAddress);

            // CInteriorProxy is not a subclass of CEntity and position data is placed at different offset
            var returnHandles = new List<int>();
            uint poolSize = s_isEnhanced ? enhancedPool->size : legacyPool->size;
            float radiusSquared = radius * radius;
            for (uint i = 0; i < poolSize; i++)
            {
                if (s_isEnhanced ? !enhancedPool->IsValid(i) : !legacyPool->IsValid(i))
                {
                    continue;
                }

                ulong address = s_isEnhanced ? enhancedPool->GetAddress(i) : legacyPool->GetAddress(i);

                float x = *(float*)(address + 0x70) - position.X;
                float y = *(float*)(address + 0x74) - position.Y;
                float z = *(float*)(address + 0x78) - position.Z;

                float distanceSquared = (x * x) + (y * y) + (z * z);
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                returnHandles.Add(s_isEnhanced ? enhancedPool->GetGuidHandleByIndex(i) : legacyPool->GetGuidHandleByIndex(i));
            }

            return returnHandles.ToArray();
        }

        public static bool BuildingHandleExists(int handle) => s_buildingPoolAddress != null ? s_isEnhanced ? ((FwBasePoolEnhanced*)(s_buildingPoolAddress))->IsHandleValid(handle) : ((FwBasePoolLegacy*)(*s_buildingPoolAddress))->IsHandleValid(handle) : false;
        public static bool AnimatedBuildingHandleExists(int handle) => s_animatedBuildingPoolAddress != null ? s_isEnhanced? ((FwBasePoolEnhanced*)(s_animatedBuildingPoolAddress))->IsHandleValid(handle) : ((FwBasePoolLegacy*)(*s_animatedBuildingPoolAddress))->IsHandleValid(handle) : false;
        public static bool InteriorInstHandleExists(int handle) => s_interiorInstPoolAddress != null ? s_isEnhanced ? ((FwBasePoolEnhanced*)(s_interiorInstPoolAddress))->IsHandleValid(handle) :((FwBasePoolLegacy*)(*s_interiorInstPoolAddress))->IsHandleValid(handle) : false;
        public static bool InteriorProxyHandleExists(int handle) => s_interiorProxyPoolAddress != null ? s_isEnhanced ? ((FwBasePoolEnhanced*)(s_interiorProxyPoolAddress))->IsHandleValid(handle) : ((FwBasePoolLegacy*)(*s_interiorProxyPoolAddress))->IsHandleValid(handle) : false;

        private static int[] GetHandlesInFwBasePool(ulong* poolAddress)
        {
            FwBasePoolEnhanced* enhancedPool = (FwBasePoolEnhanced*) poolAddress;
            FwBasePoolLegacy* legacyPool = (FwBasePoolLegacy*) *poolAddress;

            var returnHandles = new List<int>(s_isEnhanced ? enhancedPool->itemCount : legacyPool->itemCount);
            uint poolSize = s_isEnhanced? enhancedPool->size : legacyPool->size;
            for (uint i = 0; i < poolSize; i++)
            {
                if (s_isEnhanced? enhancedPool->IsValid(i) : legacyPool->IsValid(i))
                {
                    returnHandles.Add(s_isEnhanced ? enhancedPool->GetGuidHandleByIndex(i) : legacyPool->GetGuidHandleByIndex(i));
                }
            }

            return returnHandles.ToArray();
        }

        private static ulong[] GetAddressesInFwBasePool(ulong* poolAddress)
        {
            FwBasePoolEnhanced* enhancedPool = (FwBasePoolEnhanced*)poolAddress;
            FwBasePoolLegacy* legacyPool = (FwBasePoolLegacy*)*poolAddress;

            var returnAddresses = new List<ulong>(s_isEnhanced ? enhancedPool->itemCount : legacyPool->itemCount);
            uint poolSize = s_isEnhanced ? enhancedPool->size : legacyPool->size;
            for (uint i = 0; i < poolSize; i++)
            {
                if (s_isEnhanced ? enhancedPool->IsValid(i) : legacyPool->IsValid(i))
                {
                    returnAddresses.Add(s_isEnhanced ? enhancedPool->GetAddress(i) : legacyPool->GetAddress(i));
                }
            }

            return returnAddresses.ToArray();
        }

        private static int[] GetCEntityHandlesInRange(ulong* poolAddress, FVector3 position, float radius)
        {
            FwBasePoolEnhanced* enhancedPool = (FwBasePoolEnhanced*)poolAddress;
            FwBasePoolLegacy* legacyPool = (FwBasePoolLegacy*) *poolAddress;

            var returnHandles = new List<int>();
            uint poolSize = s_isEnhanced ? enhancedPool->size : legacyPool->size;
            float radiusSquared = radius * radius;
            float* entityPosition = stackalloc float[4];
            for (uint i = 0; i < poolSize; i++)
            {
                if (s_isEnhanced? !enhancedPool->IsValid(i) : !legacyPool->IsValid(i))
                {
                    continue;
                }

                ulong address = s_isEnhanced ? enhancedPool->GetAddress(i) : legacyPool->GetAddress(i);

                GetEntityPos(address, entityPosition);
                float x = entityPosition[0] - position.X;
                float y = entityPosition[1] - position.Y;
                float z = entityPosition[2] - position.Z;

                float distanceSquared = (x * x) + (y * y) + (z * z);
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                returnHandles.Add(s_isEnhanced ? enhancedPool->GetGuidHandleByIndex(i) : legacyPool->GetGuidHandleByIndex(i));
            }

            return returnHandles.ToArray();
        }

        private static ulong[] GetCEntityAddressesInRange(ulong* poolAddress, FVector3 position, float radius)
        {
            FwBasePoolEnhanced* enhancedPool = (FwBasePoolEnhanced*)poolAddress;
            FwBasePoolLegacy* legacyPool = (FwBasePoolLegacy*)*poolAddress;

            var returnAddresses = new List<ulong>();
            uint poolSize = s_isEnhanced ? enhancedPool->size : legacyPool->size;
            float radiusSquared = radius * radius;
            for (uint i = 0; i < poolSize; i++)
            {
                if (s_isEnhanced ? !enhancedPool->IsValid(i) : !legacyPool->IsValid(i))
                {
                    continue;
                }

                ulong address = s_isEnhanced ? enhancedPool->GetAddress(i) : legacyPool->GetAddress(i);

                float x = *(float*)(address + (ulong)s_pickupObjectPlacementPositionOffset) - position.X;
                float y = *(float*)(address + (ulong)s_pickupObjectPlacementPositionOffset + 4) - position.Y;
                float z = *(float*)(address + (ulong)s_pickupObjectPlacementPositionOffset + 8) - position.Z;

                float distanceSquared = (x * x) + (y * y) + (z * z);
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                returnAddresses.Add(address);
            }

            return returnAddresses.ToArray();
        }

        public static float[] GetPickupObjectPlacementPosition(IntPtr address)
        {
            float[] position = new float[4];
            ulong pickupObjectPlacementAddr = (ulong)address;
            position[0] = *(float*)(pickupObjectPlacementAddr + (ulong)s_pickupObjectPlacementPositionOffset);
            position[1] = *(float*)(pickupObjectPlacementAddr + (ulong)s_pickupObjectPlacementPositionOffset + 4);
            position[2] = *(float*)(pickupObjectPlacementAddr + (ulong)s_pickupObjectPlacementPositionOffset + 8);
            return position;
        }

        private static int CalculateAppropriateExtendedArrayLength(int[] array, int targetElementCount)
        {
            return (array.Length * 2 > targetElementCount) ? array.Length * 2 : targetElementCount * 2;
        }

        #endregion

        #region -- CPlayerInfo Data --

        private static delegate* unmanaged[Stdcall]<int, ulong> s_getPlayerPedAddressFunc;

        private static bool* s_isGameMultiplayerAddr;

        /// <summary>
        /// The offset for max health of CPlayerInfo, which is stored as an uint16_t.
        /// </summary>
        public static int CPlayerInfoMaxHealthOffset { get; }

        public static int PedPlayerInfoOffset { set; get; }
        public static int CWantedOffset { get; }
        public static int CPlayerPedTargetingOfffset { get; }

        public static int CurrentWantedLevelOffset { get; }
        /// <remarks>
        /// "current crime value" is a name we named. The canonical name of the corresponding name on `CWanted` is
        /// `m_nWantedLevel` (do not confuse with `m_WantedLevel`, which is basically the number of stars).
        /// </remarks>
        public static int CurrentCrimeValueOffset { get; }
        /// <remarks>
        /// "pending crime value" is a name we named. The canonical name of the corresponding name on `CWanted` is
        /// `m_nNewWantedLevel`.
        /// </remarks>
        public static int NewCrimeValueOffset { get; }
        public static int TimeWhenNewCrimeValueTakesEffectOffset { get; }
        public static int CWantedTimeSearchLastRefocusedOffset { get; }
        public static int CWantedTimeLastSpottedOffset { get; }
        public static int CWantedTimeHiddenEvasionStartedOffset { get; }
        public static int CWantedIgnorePlayerFlagOffset { get; }

        private static delegate* unmanaged[Stdcall]<IntPtr, void> s_activateSpecialAbilityFunc;

        // The function is for b2060 or later and static offset is for prior to b2060
        private static delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr> s_getSpecialAbilityAddressFunc;
        public static int PlayerPedSpecialAbilityOffset { get; }

        public static IntPtr GetPlayerPedAddress(int playerIndex)
        {
            return new IntPtr((long)s_getPlayerPedAddressFunc(playerIndex));
        }
        public static IntPtr GetLocalPlayerPedAddress()
        {
            return new IntPtr((long)s_getLocalPlayerPedAddressFunc());
        }
        public static int GetPlayerPedHandle(int handle)
        {
            IntPtr playerPedAddress = GetPlayerPedAddress(handle);
            return playerPedAddress != IntPtr.Zero ? GetEntityHandleFromAddress(playerPedAddress) : 0;
        }
        public static int GetLocalPlayerPedHandle()
        {
            IntPtr localPlayerPedAddress = GetLocalPlayerPedAddress();
            return localPlayerPedAddress != IntPtr.Zero ? GetEntityHandleFromAddress(localPlayerPedAddress) : 0;
        }
        public static int GetLocalPlayerIndex()
        {
            if (s_isGameMultiplayerAddr == null || *s_isGameMultiplayerAddr)
            {
                // A fallback path if the variable could not found to make sure the same value will be returned as what PLAYER_ID returns, an extreme edge case if the variable was found
                // You even have to disable SHV to call NETWORK_GET_NUM_CONNECTED_PLAYERS (for preventing the game from going Online) before custom scripts (for enabling multiplayer) can use features for multiplayer
                return GetLocalPlayerIndexViaNativeCall();
            }

            // The same value as what PLAYER_ID returns if the game mode is singleplayer and not multiplayer
            return 0;

            static int GetLocalPlayerIndexViaNativeCall()
            {
                ulong* resultAddr = NativeFunc.Invoke(0x4F8644AF03D0E0D6 /* PLAYER_ID */, null, 0);
                if (resultAddr == null)
                {
                    throw new InvalidOperationException("Game.Player can only be called from the main thread.");
                }

                return *(int*)resultAddr;
            }
        }

        public static IntPtr GetCPlayerInfoAddress(int playerIndex)
        {
            if (PedPlayerInfoOffset == 0)
            {
                return IntPtr.Zero;
            }

            IntPtr playerPedAddr = GetPlayerPedAddress(playerIndex);
            if (playerPedAddr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            long* playerInfoAddr = *(long**)((ulong)playerPedAddr + (uint)PedPlayerInfoOffset);
            if (playerPedAddr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return new IntPtr(playerInfoAddr);
        }
        public static IntPtr GetCPlayerPedTargetingAddress(int playerIndex)
        {
            if (CPlayerPedTargetingOfffset == 0)
            {
                return IntPtr.Zero;
            }

            IntPtr cPlayerInfoAddr = GetCPlayerInfoAddress(playerIndex);
            if (cPlayerInfoAddr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return new IntPtr((long)((ulong)cPlayerInfoAddr + (uint)CPlayerPedTargetingOfffset));
        }
        public static IntPtr GetCWantedAddress(int playerIndex)
        {
            if (CWantedOffset == 0)
            {
                return IntPtr.Zero;
            }

            IntPtr cPlayerInfoAddr = GetCPlayerInfoAddress(playerIndex);
            if (cPlayerInfoAddr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return new IntPtr((long)((ulong)cPlayerInfoAddr + (uint)CWantedOffset));
        }

        public static int GetFreeAimBuildingTargetHandleOfPlayer(int playerIndex)
        {
            IntPtr playerPedTargetingAddr = GetCPlayerPedTargetingAddress(playerIndex);
            if (playerPedTargetingAddr == IntPtr.Zero)
            {
                return 0;
            }

            // Return zero if the targeted `CEntity` address is null or is not null but an instance other than
            // `CBuilding`.
            // Should be a `CPhysical` address in that case since the value is null if the player is aiming a
            // `CAnimatedBuilding` instance (e.g. the fan at Ammu-Nation in Little Seoul).
            ulong freeAimTargetAddr = *(ulong*)(playerPedTargetingAddr + 0x110);
            if (freeAimTargetAddr == 0 || *(byte*)(freeAimTargetAddr + 0x28) != 1)
            {
                return 0;
            }

            return GetBuildingHandleFromAddress(new IntPtr((long)freeAimTargetAddr));
        }

        /// <summary>
        /// Activates the special ability for the player.
        /// </summary>
        /// <remarks>
        /// This function is for v1.0.617.1 and earlier versions, where the native function SPECIAL_ABILITY_ACTIVATE is not present.
        /// </remarks>
        public static void ActivateSpecialAbility(int playerIndex)
        {
            IntPtr specialAbilityAddr = GetPrimarySpecialAbilityStructAddress(playerIndex);
            if (specialAbilityAddr == IntPtr.Zero)
            {
                return;
            }

            var task = new ActivateSpecialAbilityTask(specialAbilityAddr);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);
        }
        public static IntPtr GetPrimarySpecialAbilityStructAddress(int playerIndex)
        {
            IntPtr playerPedAddress = GetPlayerPedAddress(playerIndex);

            if (playerPedAddress == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            // Two special ability slots are available in b2060 and later versions
            if (s_isEnhanced || GetGameVersion() >= 59)
            {
                if (s_getSpecialAbilityAddressFunc == null)
                {
                    return IntPtr.Zero;
                }

                return s_getSpecialAbilityAddressFunc(playerPedAddress, 0);
            }
            else
            {
                if (PlayerPedSpecialAbilityOffset == 0)
                {
                    return IntPtr.Zero;
                }

                return new IntPtr(*(long**)((ulong)playerPedAddress + (uint)PlayerPedSpecialAbilityOffset));
            }
        }

        internal sealed class ActivateSpecialAbilityTask : IScriptTask
        {
            #region Fields
            internal IntPtr _specialAbilityStructAddress;
            #endregion

            internal ActivateSpecialAbilityTask(IntPtr specialAbilityStructAddress)
            {
                this._specialAbilityStructAddress = specialAbilityStructAddress;
            }

            public void Run()
            {
                s_activateSpecialAbilityFunc(_specialAbilityStructAddress);
            }
        }

        #endregion

        #region -- CPathFind Data --

        public static unsafe class PathFind
        {
            private static ulong s_cPathFindInstanceAddress;

            static PathFind()
            {
                byte* address;
                if (s_isEnhanced)
                {
                    address = MemScanner.FindPatternBmh("48 8b 44 24 ? 8d 68 ? 4c 8d 35");
                    if (address != null)
                    {
                        s_cPathFindInstanceAddress = (ulong)(*(int*)(address + 11) + address + 15);
                    }
                }
                else
                {
                    address = MemScanner.FindPatternBmh("\x4D\x8B\xF0\x45\x8A\xE1\x48\x8B\xF9\x4C\x8D\x05", "xxxxxxxxxxxx");
                    if (address != null)
                    {
                        s_cPathFindInstanceAddress = (ulong)(*(int*)(address + 12) + address + 16);
                    }
                }
            }

            // These values hasn't been changed between b372 and b2845
            private const int StartPathNodeOffsetOfCPathFind = 0x1640;
            private const int MaxCPathRegionCount = 0x400;

            [StructLayout(LayoutKind.Explicit, Size = 0x70)]
            internal struct CPathRegion
            {
                [FieldOffset(0x10)]
                internal IntPtr NodeArrayPtr;
                [FieldOffset(0x18)]
                internal uint NodeCount;
                [FieldOffset(0x1C)]
                internal uint NodeCountVehicle;
                [FieldOffset(0x20)]
                internal uint NodeCountPed;
                [FieldOffset(0x28)]
                internal IntPtr NodeLinkArrayPtr;
                [FieldOffset(0x30)]
                internal uint NodeLinkCount;
                [FieldOffset(0x38)]
                internal IntPtr VirtualJunctionArrayPtr;
                [FieldOffset(0x40)]
                internal IntPtr HeightSampleArrayPtr;

                // `CPathRegion.JunctionMap` is at 0x50, which has a `rage::CPathRegion::JunctionMapContainer`.
                // `rage::CPathRegion::JunctionMapContainer` is practically an alias of
                // `rage::atBinaryMap<int,unsigned int>`. `rage::atBinaryMap` internally has a bool (at the 0x0 offset)
                // that represents whether the content is sorted before the `rage::atArray` field.

                [FieldOffset(0x60)]
                internal uint JunctionCount;
                [FieldOffset(0x64)]
                internal uint HeightSampleCount;

                internal CPathNode* GetPathNode(uint nodeId)
                {
                    if (NodeArrayPtr == IntPtr.Zero && nodeId >= NodeCount)
                    {
                        return null;
                    }

                    return GetPathNodeUnsafe(nodeId);
                }
                internal CPathNode* GetPathNodeUnsafe(uint nodeId) => (CPathNode*)((ulong)NodeArrayPtr + nodeId * (uint)sizeof(CPathNode));

                internal CPathNodeLink* GetPathNodeLink(uint index)
                {
                    if (NodeLinkArrayPtr == IntPtr.Zero && index >= NodeLinkCount)
                    {
                        return null;
                    }

                    return GetPathNodeLinkUnsafe(index);
                }
                internal CPathNodeLink* GetPathNodeLinkUnsafe(uint index) => (CPathNodeLink*)((ulong)NodeLinkArrayPtr + index * (uint)sizeof(CPathNodeLink));
            }

            private static CPathRegion* GetCPathRegion(uint areaId)
            {
                if (areaId >= MaxCPathRegionCount || s_cPathFindInstanceAddress == 0)
                {
                    return null;
                }

                return *(CPathRegion**)(s_cPathFindInstanceAddress + StartPathNodeOffsetOfCPathFind + areaId * 0x8);
            }

            public enum VehiclePathNodeProperties
            {
                None = 0,
                OffRoad = 1,
                OnPlayersRoad = 2,
                NoBigVehicles = 4,
                SwitchedOff = 8,
                TunnelOrInterior = 16,
                LeadsToDeadEnd = 32,
                /// <summary>
                /// <see cref="Boat"/> takes precedence over this flag.
                /// </summary>
                Highway = 64,
                Junction = 128,
                /// <summary>
                /// Cannot be used with <see cref="GiveWay"/>, because vehicle nodes can have either traffic-light or give-way feature as a special function but cannot have both of them.
                /// </summary>
                TrafficLight = 256,
                /// <summary>
                /// Cannot be used with <see cref="TrafficLight"/>, because vehicle nodes can have either traffic-light or give-way feature as a special function but cannot have both of them.
                /// </summary>
                GiveWay = 512,
                /// <summary>
                /// Cannot be used with <see cref="Highway"/>.
                /// </summary>
                Boat = 1024,

                // GET_VEHICLE_NODE_PROPERTIES will not set any of the values below set as flags
                DontAllowGps = 2048,
            }

            [StructLayout(LayoutKind.Explicit, Size = 0x28)]
            internal struct CPathNode
            {
                [FieldOffset(0x0)]
                internal CPathNode* Next;
                [FieldOffset(0x8)]
                internal CPathNode* Previous;

                // Note: CPathNode in the game is supposed to have rage::CNodeAddress (4-byte union, which has
                // a regular uint32_t field and bit fields) at 0x10
                [FieldOffset(0x10)]
                internal ushort AreaId;
                [FieldOffset(0x12)]
                internal ushort NodeId;

                [FieldOffset(0x14)]
                internal uint StreetNameHash;

                [FieldOffset(0x1A)]
                internal ushort startIndexOfLinks;

                // These 2 fields should be multiplied by 4 when you convert back to float
                [FieldOffset(0x1C)]
                internal short PositionX;
                [FieldOffset(0x1E)]
                internal short PositionY;

                [FieldOffset(0x20)]
                internal ushort Flags1;

                // This field should be multiplied by 32 when you convert back to float
                [FieldOffset(0x22)]
                internal short PositionZ;

                [FieldOffset(0x24)]
                internal byte Flags2;
                [FieldOffset(0x25)]
                internal byte Flags3AndLinkCount;
                [FieldOffset(0x26)]
                internal byte Flags4;
                // 1st to 4th bits are used for density
                [FieldOffset(0x27)]
                internal byte Flag5AndDensity;

                internal int Density => Flag5AndDensity & 0xF;

                internal int LinkCount => Flags3AndLinkCount >> 3;

                // Native functions for path nodes get area IDs and node IDs from the values subtracted by one from passed values
                // When the lower half of bits (of passed values) are equal to zero, the natives considers the null handle is passed
                internal int GetHandleForNativeFunctions() => ((NodeId << 0x10) + AreaId + 1);

                internal FVector3 UncompressedPosition => new FVector3((float)PositionX / 4, (float)PositionY / 4, (float)PositionZ / 32);

                internal bool IsSwitchedOff
                {
                    get => (Flags2 & 0x80) != 0;
                    set
                    {
                        if (value)
                        {
                            Flags2 |= 0x80;
                        }
                        else
                        {
                            Flags2 &= 0x7F;
                        }
                    }
                }

                /// <summary>
                /// Get property flags in almost the same way as GET_VEHICLE_NODE_PROPERTIES returns flags as the 5th parameter (seems the flags the native returns will never contain the 1024 flag).
                /// </summary>
                internal VehiclePathNodeProperties GetPropertyFlags()
                {
                    // for those wondering the proper implementation in GET_VEHICLE_NODE_PROPERTIES, you can find it with "41 0F B6 40 27 83 E0 0F 89 07 41 F6 40 20 08" (tested with b372, b2699, and b2944)

                    VehiclePathNodeProperties propertyFlags = VehiclePathNodeProperties.None;
                    if ((Flags1 & 8) != 0)
                    {
                        propertyFlags |= VehiclePathNodeProperties.OffRoad;
                    }
                    if ((Flags1 & 0x10) != 0)
                    {
                        propertyFlags |= VehiclePathNodeProperties.OnPlayersRoad;
                    }
                    if ((Flags1 & 0x20) != 0)
                    {
                        propertyFlags |= VehiclePathNodeProperties.NoBigVehicles;
                    }
                    if ((Flags2 & 0x80) != 0)
                    {
                        propertyFlags |= VehiclePathNodeProperties.SwitchedOff;
                    }
                    if ((Flags4 & 0x1) != 0)
                    {
                        propertyFlags |= VehiclePathNodeProperties.TunnelOrInterior;
                    }
                    // equivalent to "if (*(uint32_t*)(CPathNode + 36) & 0x70000000)" in C or C++
                    if ((Flag5AndDensity & 0x70) != 0)
                    {
                        propertyFlags |= VehiclePathNodeProperties.LeadsToDeadEnd;
                    }
                    // The water/boat bit takes precedence over this highway flag
                    if (((Flags2 & 0x40) != 0 || (Flags2 & 0x20) == 0))
                    {
                        propertyFlags |= VehiclePathNodeProperties.Highway;
                    }
                    if (((Flags2 >> 8) & 1) != 0)
                    {
                        propertyFlags |= VehiclePathNodeProperties.Junction;
                    }
                    if ((Flags1 & 0xF800) == 0x7800)
                    {
                        propertyFlags |= VehiclePathNodeProperties.TrafficLight;
                    }
                    if ((Flags1 & 0xF800) == 0x8000)
                    {
                        propertyFlags |= VehiclePathNodeProperties.GiveWay;
                    }
                    if ((Flags2 & 0x20) != 0)
                    {
                        propertyFlags |= VehiclePathNodeProperties.Boat;
                    }
                    if ((Flags2 & 1) != 0)
                    {
                        propertyFlags |= VehiclePathNodeProperties.DontAllowGps;
                    }

                    return propertyFlags;
                }

                internal bool IsInArea(float x1, float y1, float z1, float x2, float y2, float z2)
                {
                    float posXUncompressed = (float)PositionX / 4;
                    float posYUncompressed = (float)PositionY / 4;
                    float posZUncompressed = (float)PositionZ / 32;

                    if (posXUncompressed < x1 || posXUncompressed > x2)
                    {
                        return false;
                    }
                    if (posYUncompressed < y1 || posYUncompressed > y2)
                    {
                        return false;
                    }
                    if (posZUncompressed < z1 || posYUncompressed > z2)
                    {
                        return false;
                    }

                    return true;
                }
                internal bool IsInCircle(float x, float y, float z, float radius)
                {
                    float posXUncompressed = (float)PositionX / 4;
                    float posYUncompressed = (float)PositionY / 4;
                    float posZUncompressed = (float)PositionZ / 32;

                    float deltaX = (float)x - posXUncompressed;
                    float deltaY = (float)y - posYUncompressed;
                    float deltaZ = (float)z - posZUncompressed;

                    return ((deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ)) <= radius * radius;
                }
            }

            public static IntPtr GetPathNodeAddress(int handle)
            {
                GetCorrectedNodeAndAreaIdFromPathNodeHandle(handle, out uint areaId, out uint nodeId);

                CPathRegion* pathRegion = GetCPathRegion(areaId);
                if (pathRegion == null)
                {
                    return IntPtr.Zero;
                }

                return new IntPtr(pathRegion->GetPathNode(nodeId));
            }

            public static IntPtr GetPathNodeLinkAddress(int areaId, int nodeLinkIndex)
            {
                CPathRegion* pathRegion = GetCPathRegion((uint)areaId);
                if (pathRegion == null)
                {
                    return IntPtr.Zero;
                }

                return new IntPtr(pathRegion->GetPathNodeLink((uint)nodeLinkIndex));
            }

            private static void GetCorrectedNodeAndAreaIdFromPathNodeHandle(int handleForNatives, out uint areaId, out uint nodeId)
            {
                uint handleCorrected = (uint)handleForNatives - 1;
                areaId = (ushort)(handleCorrected & 0xFFFF);
                nodeId = (ushort)(handleCorrected >> 0x10);
            }

            public static FVector3 GetPathNodePosition(int handle)
            {
                IntPtr pathNode = GetPathNodeAddress(handle);
                if (pathNode == null)
                {
                    return default;
                }

                return ((CPathNode*)pathNode)->UncompressedPosition;
            }

            public static int GetVehiclePathNodeDensity(int handle)
            {
                IntPtr pathNode = GetPathNodeAddress(handle);
                if (pathNode == null)
                {
                    return 0;
                }

                return ((CPathNode*)pathNode)->Density;
            }

            public static int GetVehiclePathNodePropertyFlags(int handle)
            {
                IntPtr pathNode = GetPathNodeAddress(handle);
                if (pathNode == null)
                {
                    return 0;
                }

                return (int)((CPathNode*)pathNode)->GetPropertyFlags();
            }

            public static bool GetPathNodeSwitchedOffFlag(int handle)
            {
                IntPtr pathNode = GetPathNodeAddress(handle);
                if (pathNode == null)
                {
                    return false;
                }

                return ((CPathNode*)pathNode)->IsSwitchedOff;
            }

            public static void SetPathNodeSwitchedOffFlag(int handle, bool toggle)
            {
                IntPtr pathNode = GetPathNodeAddress(handle);
                if (pathNode == null)
                {
                    return;
                }

                ((CPathNode*)pathNode)->IsSwitchedOff = toggle;
            }

            [StructLayout(LayoutKind.Explicit, Size = 0x8)]
            internal struct CPathNodeLink
            {
                // Same as CPathNode, this field is supposed to be a rage::CNodeAddress...
                [FieldOffset(0x0)]
                internal ushort AreaId;
                [FieldOffset(0x2)]
                internal ushort NodeId;

                [FieldOffset(0x4)]
                internal byte Flags0;
                [FieldOffset(0x5)]
                internal byte Flags1;
                [FieldOffset(0x6)]
                internal byte Flags2;
                [FieldOffset(0x7)]
                internal byte LinkLength;

                internal int ForwardLaneCount => (Flags2 >> 5) & 7;
                internal int BackwardLaneCount => (Flags2 >> 2) & 7;

                internal void GetForwardAndBackwardCount(out int forwardCount, out int backwardCount)
                {
                    forwardCount = (Flags2 >> 5) & 7;
                    backwardCount = (Flags2 >> 2) & 7;
                }

                internal void GetTargetAreaAndNodeId(out int areaId, out int nodeId)
                {
                    areaId = AreaId;
                    nodeId = NodeId;
                }
            }

            // Use this buffer when we get all loaded path nodes to avoid allocating new large objects for buffer space and costing a significant time, since the number of path node handles can even exceed more than 21250
            // On the other hand, each vanilla ynd file (each ynd file has nodes in an area of 512 meters x 512 meters) contains likely 100 to 1500 nodes, and getting nodes nearby 512 meters will likely get less than 1500 nodes.
            // Therefore, using this buffer won't make much difference in how many CPU cycles will be used when we get nodes in certain area
            private static List<int> s_pathNodeBuffer = new List<int>();
            // The buffer can be too big to dispose fast enough (by considering a large object heap), so use a lock
            // instead of using a local variable
            private static readonly object s_pathNodeBufferLock = new();

            public static int[] GetAllLoadedVehicleNodes(Func<int, bool> predicateForFlags)
            {
                lock (s_pathNodeBufferLock)
                {
                    s_pathNodeBuffer.Clear();

                    for (uint i = 0; i < MaxCPathRegionCount; i++)
                    {
                        CPathRegion* pathRegion = GetCPathRegion(i);
                        if (pathRegion == null || pathRegion->NodeArrayPtr == IntPtr.Zero)
                        {
                            continue;
                        }

                        uint vehicleNodeCountInRegion = pathRegion->NodeCountVehicle;
                        for (uint j = 0; j < vehicleNodeCountInRegion; j++)
                        {
                            CPathNode* pathNode = pathRegion->GetPathNodeUnsafe(j);
                            if (predicateForFlags == null || predicateForFlags((int)pathNode->GetPropertyFlags()))
                            {
                                s_pathNodeBuffer.Add(pathNode->GetHandleForNativeFunctions());
                            }
                        }
                    }

                    return s_pathNodeBuffer.ToArray();
                }
            }

            public static int[] GetLoadedVehicleNodesInRange(float x, float y, float z, float radius, Func<int, bool> predicateForFlags)
            {
                var result = new List<int>();

                foreach (uint areaId in GetAreaIdsInRange(x, y, radius))
                {
                    CPathRegion* pathRegion = GetCPathRegion(areaId);
                    if (pathRegion == null || pathRegion->NodeArrayPtr == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint vehicleNodeCountInRegion = pathRegion->NodeCountVehicle;
                    for (uint j = 0; j < vehicleNodeCountInRegion; j++)
                    {
                        CPathNode* vehPathNode = pathRegion->GetPathNodeUnsafe(j);
                        if (!CheckVehPathNodePropertyPredicateAndPosition(vehPathNode, predicateForFlags, x, y, z, radius))
                        {
                            continue;
                        }

                        result.Add(vehPathNode->GetHandleForNativeFunctions());
                    }
                }

                return result.ToArray();
            }

            public static int GetClosestLoadedVehiclePathNode(float x, float y, float z, float radius, Func<int, bool> predicateForFlags)
            {
                int result = 0;
                float closestDistance = 3e38f;

                foreach (uint areaId in GetAreaIdsInRange(x, y, radius))
                {
                    CPathRegion* pathRegion = GetCPathRegion(areaId);
                    if (pathRegion == null || pathRegion->NodeArrayPtr == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint vehicleNodeCountInRegion = pathRegion->NodeCountVehicle;
                    for (uint j = 0; j < vehicleNodeCountInRegion; j++)
                    {
                        CPathNode* vehPathNode = pathRegion->GetPathNodeUnsafe(j);
                        if (!CheckVehPathNodePropertyPredicateAndPosition(vehPathNode, predicateForFlags, x, y, z, radius))
                        {
                            continue;
                        }

                        FVector3 nodePos = vehPathNode->UncompressedPosition;
                        float nodeDist = DistanceToSquared(x, y, z, nodePos.X, nodePos.Y, nodePos.Z);
                        if (nodeDist < closestDistance)
                        {
                            result = vehPathNode->GetHandleForNativeFunctions();
                            closestDistance = nodeDist;
                        }
                    }
                }

                return result;
            }

            public static int[] GetLoadedVehicleNodesInArea(float x1, float y1, float z1, float x2, float y2, float z2, Func<int, bool> predicateForFlags)
            {
                float minX = Math.Min(x1, x2);
                float minY = Math.Min(y1, y2);
                float minZ = Math.Min(z1, z2);
                float maxX = Math.Max(x1, x2);
                float maxY = Math.Max(y1, y2);
                float maxZ = Math.Max(z1, z2);

                var result = new List<int>();

                foreach (uint areaId in GetAreaIdsInArea(minX, minY, maxX, maxY))
                {
                    CPathRegion* pathRegion = GetCPathRegion(areaId);
                    if (pathRegion == null || pathRegion->NodeArrayPtr == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint vehicleNodeCountInRegion = pathRegion->NodeCountVehicle;
                    for (uint j = 0; j < vehicleNodeCountInRegion; j++)
                    {
                        CPathNode* vehPathNode = pathRegion->GetPathNodeUnsafe(j);

                        if (!CheckVehPathNodePropertyPredicateAndPosition(vehPathNode, predicateForFlags, minX, minY, minZ, maxX, maxY, maxZ))
                        {
                            continue;
                        }

                        result.Add(vehPathNode->GetHandleForNativeFunctions());
                    }
                }

                return result.ToArray();
            }

            public static int[] GetPathNodeLinkIndicesOfPathNode(int handleOfPathNode)
            {
                GetCorrectedNodeAndAreaIdFromPathNodeHandle(handleOfPathNode, out uint areaId, out uint nodeId);

                CPathRegion* pathRegion = GetCPathRegion(areaId);
                if (pathRegion == null)
                {
                    return Array.Empty<int>();
                }

                CPathNode* pathNode = pathRegion->GetPathNode(nodeId);
                if (pathNode == null)
                {
                    return Array.Empty<int>();
                }

                ushort pathNodeLinkStartId = pathNode->startIndexOfLinks;
                int pathNodeLinkCount = pathNode->LinkCount;

                int[] result = new int[pathNodeLinkCount];
                for (int i = 0; i < pathNodeLinkCount; i++)
                {
                    result[i] = pathNodeLinkStartId + i;
                }
                return result;
            }

            public static bool GetPathNodeLinkLanes(int areaId, int nodeLinkIndex, out int forwardLaneCount, out int backwardLaneCount)
            {
                CPathRegion* pathRegion = GetCPathRegion((uint)areaId);
                if (pathRegion == null)
                {
                    forwardLaneCount = 0;
                    backwardLaneCount = 0;

                    return false;
                }

                CPathNodeLink* pathNodeLink = pathRegion->GetPathNodeLink((uint)nodeLinkIndex);
                if (pathNodeLink == null)
                {
                    forwardLaneCount = 0;
                    backwardLaneCount = 0;

                    return false;
                }

                pathNodeLink->GetForwardAndBackwardCount(out forwardLaneCount, out backwardLaneCount);
                return true;
            }

            public static bool GetTargetAreaAndNodeIdToTargetNode(int areaIdOfNodeLink, int nodeLinkIndex, out int targetAreaId, out int targetNodeId)
            {
                CPathRegion* pathRegion = GetCPathRegion((uint)areaIdOfNodeLink);
                if (pathRegion == null)
                {
                    targetAreaId = 0;
                    targetNodeId = 0;

                    return false;
                }

                CPathNodeLink* pathNodeLink = pathRegion->GetPathNodeLink((uint)nodeLinkIndex);
                if (pathNodeLink == null)
                {
                    targetAreaId = 0;
                    targetNodeId = 0;

                    return false;
                }

                pathNodeLink->GetTargetAreaAndNodeId(out targetAreaId, out targetNodeId);
                return true;
            }

            public static int GetTargetNodeHandleFromNodeLink(int areaIdOfNodeLink, int nodeLinkIndex)
            {
                CPathRegion* pathRegionOfNodeLink = GetCPathRegion((uint)areaIdOfNodeLink);
                if (pathRegionOfNodeLink == null)
                {
                    return 0;
                }
                CPathNodeLink* pathNodeLink = pathRegionOfNodeLink->GetPathNodeLink((uint)nodeLinkIndex);
                if (pathNodeLink == null)
                {
                    return 0;
                }

                pathNodeLink->GetTargetAreaAndNodeId(out int targetAreaId, out int targetNodeId);
                CPathRegion* pathRegionOfTargetNode = GetCPathRegion((uint)targetAreaId);
                if (pathRegionOfTargetNode == null)
                {
                    return 0;
                }
                CPathNode* targetPathNode = pathRegionOfTargetNode->GetPathNode((uint)targetNodeId);
                if (targetPathNode == null)
                {
                    return 0;
                }

                return targetPathNode->GetHandleForNativeFunctions();
            }

            private static bool CheckVehPathNodePropertyPredicateAndPosition(CPathNode* vehPathNode, Func<int, bool> predicateForFlags, float x, float y, float z, float maxDistRadius)
            {
                if (predicateForFlags != null && !predicateForFlags((int)vehPathNode->GetPropertyFlags()))
                {
                    return false;
                }
                if (!vehPathNode->IsInCircle(x, y, z, maxDistRadius))
                {
                    return false;
                }

                return true;
            }

            private static bool CheckVehPathNodePropertyPredicateAndPosition(CPathNode* vehPathNode, Func<int, bool> predicateForFlags, float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
            {
                if (predicateForFlags != null && !predicateForFlags((int)vehPathNode->GetPropertyFlags()))
                {
                    return false;
                }
                if (!vehPathNode->IsInArea(minX, minY, minZ, maxX, maxY, maxZ))
                {
                    return false;
                }

                return true;
            }

            private static IEnumerable<uint> GetAreaIdsInArea(float x1, float y1, float x2, float y2)
            {
                float minX = Math.Min(x1, x2);
                float minY = Math.Min(y1, y2);
                float maxX = Math.Max(x1, x2);
                float maxY = Math.Max(y1, y2);

                int minAreaRegionX = CalcIndexComponentOfAreaId(minX);
                int minAreaRegionY = CalcIndexComponentOfAreaId(minY);
                int maxAreaRegionX = CalcIndexComponentOfAreaId(maxX);
                int maxAreaRegionY = CalcIndexComponentOfAreaId(maxY);

                int areaIdCount = (maxAreaRegionX - minAreaRegionX + 1) * (maxAreaRegionY - minAreaRegionY + 1);

                for (int regionY = minAreaRegionY; regionY <= maxAreaRegionY; regionY++)
                {
                    for (int regionX = minAreaRegionX; regionX <= maxAreaRegionX; regionX++)
                    {
                        yield return ComposeAreaIdByIndex(regionX, regionY);
                    }
                }
            }

            private static IEnumerable<uint> GetAreaIdsInRange(float x, float y, float radius)
            {
                float rectMinX = x - radius;
                float rectMinY = y - radius;
                float rectMaxX = x + radius;
                float rectMaxY = y + radius;

                int minAreaRegionXIndex = CalcIndexComponentOfAreaId(rectMinX);
                int minAreaRegionYIndex = CalcIndexComponentOfAreaId(rectMinY);
                int maxAreaRegionXIndex = CalcIndexComponentOfAreaId(rectMaxX);
                int maxAreaRegionYIndex = CalcIndexComponentOfAreaId(rectMaxY);

                for (int regionYIndex = minAreaRegionYIndex; regionYIndex <= maxAreaRegionYIndex; regionYIndex++)
                {
                    for (int regionXIndex = minAreaRegionXIndex; regionXIndex <= maxAreaRegionXIndex; regionXIndex++)
                    {
                        float currectRegionMinXBound = (float)regionXIndex * 512 - 8192f;
                        float currectRegionMinYBound = (float)regionYIndex * 512 - 8192f;
                        float currectRegionMaxXBound = currectRegionMinXBound + 512f;
                        float currectRegionMaxYBound = currectRegionMinYBound + 512f;

                        if (DoCircleAndRectIntersectOrTouch(radius, x, y, currectRegionMinXBound, currectRegionMinYBound, currectRegionMaxXBound, currectRegionMaxYBound))
                        {
                            yield return ComposeAreaIdByIndex(regionXIndex, regionYIndex);
                        }
                    }
                }
            }

            private static int CalcIndexComponentOfAreaId(float val)
            {
                int indexUnclamped = (int)((val + 8192f) / 512);
                return Math.Min(Math.Max(indexUnclamped, 0), 31);
            }
            private static uint ComposeAreaIdByIndex(int x, int y) => (uint)(x + y * 0x20);

            // Nodes at bound can be included in either area (e.g. a vehicle node at (0, 263, 10) can be included in either of the ynd files for the area IDs 527 (0x20F) or 528 (0x210))
            private static bool DoCircleAndRectIntersectOrTouch(float radius, float xCenter, float yCenter, float x1, float y1, float x2, float y2)
            {
                // Nearest position will be calculated wrong if x1 and y2 parameters are passed as x2 and y2 and vice versa
                float nearestX = Math.Max(x1, Math.Min(xCenter, x2));
                float nearestY = Math.Max(y1, Math.Min(yCenter, y2));
                float deltaX = xCenter - nearestX;
                float deltaY = yCenter - nearestY;

                return (deltaX * deltaX + deltaY * deltaY) <= (radius * radius);
            }
        }

        #endregion

        #region -- Radar Blip Pool --

        private static ulong* s_radarBlipPoolAddress;
        private static int* s_possibleRadarBlipCountAddress;
        private static int* s_unkFirstRadarBlipIndexAddress;
        private static int* s_northRadarBlipHandleAddress;
        private static int* s_centerRadarBlipHandleAddress;

        private static bool CheckBlip(ulong blipAddress, FVector3? position, float radius, params int[] spriteTypes)
        {
            if (spriteTypes.Length > 0)
            {
                int spriteIndex = *(int*)(blipAddress + 0x40);
                if (!Array.Exists(spriteTypes, x => x == spriteIndex))
                {
                    return false;
                }
            }

            if (position == null || !(radius > 0f))
            {
                return true;
            }

            FVector3 positionNonNullable = position.GetValueOrDefault();
            float* blipPosition = stackalloc float[3];

            blipPosition[0] = *(float*)(blipAddress + 0x10);
            blipPosition[1] = *(float*)(blipAddress + 0x14);
            blipPosition[2] = *(float*)(blipAddress + 0x18);

            float x = blipPosition[0] - positionNonNullable.X;
            float y = blipPosition[1] - positionNonNullable.Y;
            float z = blipPosition[2] - positionNonNullable.Z;
            float distanceSquared = (x * x) + (y * y) + (z * z);
            float radiusSquared = radius * radius;

            return distanceSquared <= radiusSquared;
        }

        // The equivalent function is called in 2 functions (which is for the north and player blip) used in GET_NUMBER_OF_ACTIVE_BLIPS
        private static short GetBlipIndexIfHandleIsValid(int handle)
        {
            if (handle == 0)
            {
                return -1;
            }
            ushort blipIndex = (ushort)handle;
            ulong blipAddress = *(s_radarBlipPoolAddress + blipIndex);
            if (blipAddress == 0)
            {
                return -1;
            }

            int blipCreationIncrement = (handle >> 0x10);
            if (blipCreationIncrement != *(int*)(blipAddress + 0x8))
            {
                return -1;
            }

            return (short)blipIndex;
        }
        public static int[] GetNonCriticalRadarBlipHandles(params int[] spriteTypes)
        {
            return GetNonCriticalRadarBlipHandles(null, 0f, spriteTypes);
        }
        public static int[] GetNonCriticalRadarBlipHandles(FVector3? position = default, float radius = 0f, params int[] spriteTypes)
        {
            if (s_radarBlipPoolAddress == null)
            {
                return Array.Empty<int>();
            }

            int possibleBlipCount = *s_possibleRadarBlipCountAddress;
            int unkFirstBlipIndex = *s_unkFirstRadarBlipIndexAddress;
            int northBlipIndex = GetBlipIndexIfHandleIsValid(*s_northRadarBlipHandleAddress);
            int centerBlipIndex = GetBlipIndexIfHandleIsValid(*s_centerRadarBlipHandleAddress);

            var handles = new List<int>(possibleBlipCount);

            // Skip the 3 critical blips, just like GET_FIRST_BLIP_INFO_ID does
            // The 3 critical blips is the north blip, the center blip, and the unknown simple blip (placeholder?).
            for (int i = 0; i < possibleBlipCount; i++)
            {
                ulong address = *(s_radarBlipPoolAddress + i);

                if (address == 0 || i == unkFirstBlipIndex || i == northBlipIndex || i == centerBlipIndex)
                {
                    continue;
                }

                if (!CheckBlip(address, position, radius, spriteTypes))
                {
                    continue;
                }

                ushort blipCreationIncrement = *(ushort*)(address + 8);
                handles.Add((int)((blipCreationIncrement << 0x10) + (uint)i));
            }

            return handles.ToArray();
        }

        public static int GetNorthBlip() => s_northRadarBlipHandleAddress != null ? *s_northRadarBlipHandleAddress : 0;

        public static IntPtr GetBlipAddress(int handle)
        {
            if (s_radarBlipPoolAddress == null)
            {
                return IntPtr.Zero;
            }

            int poolIndexOfHandle = handle & 0xFFFF;
            int possibleBlipCount = *s_possibleRadarBlipCountAddress;

            if (poolIndexOfHandle >= possibleBlipCount)
            {
                return IntPtr.Zero;
            }

            ulong address = *(s_radarBlipPoolAddress + poolIndexOfHandle);

            if (address != 0 && IsBlipCreationIncrementValid(address, handle))
            {
                return new IntPtr((long)address);
            }

            return IntPtr.Zero;

            bool IsBlipCreationIncrementValid(ulong blipAddress, int blipHandle) => *(ushort*)(blipAddress + 8) == (((uint)blipHandle >> 0x10));
        }

        #endregion

        #region -- CScriptResource Data --

        internal enum CScriptResourceTypeNameIndex : ushort
        {
            Checkpoint = 6,
            RelGroup = 20,
            ScaleformMovie = 21,
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct CGameScriptResource
        {
            [FieldOffset(0x0)]
            internal ulong* vTable;
            [FieldOffset(0x8)]
            internal CScriptResourceTypeNameIndex resourceTypeNameIndex;
            [FieldOffset(0xC)]
            internal uint counterOfPool;
            [FieldOffset(0x10)]
            internal uint indexOfPool;
            [FieldOffset(0x18)]
            internal CGameScriptResource* next;
            [FieldOffset(0x20)]
            internal CGameScriptResource* prev;
        }

        internal sealed class GetAllCScriptResourceHandlesTask : IScriptTask
        {
            #region Fields
            internal CScriptResourceTypeNameIndex _typeNameIndex;
            internal int[] _returnHandles = Array.Empty<int>();
            #endregion

            internal GetAllCScriptResourceHandlesTask(CScriptResourceTypeNameIndex typeNameIndex)
            {
                this._typeNameIndex = typeNameIndex;
            }

            public void Run()
            {
                ulong cGameScriptHandlerAddress = s_getCGameScriptHandlerAddressFunc();

                if (cGameScriptHandlerAddress == 0)
                {
                    return;
                }

                List<int> handles = new List<int>();
                CGameScriptResource* firstRegisteredScriptResourceItem = *(CGameScriptResource**)(cGameScriptHandlerAddress + 48);
                for (CGameScriptResource* item = firstRegisteredScriptResourceItem; item != null; item = item->next)
                {
                    if (item->resourceTypeNameIndex != _typeNameIndex)
                    {
                        continue;
                    }

                    handles.Add((int)item->counterOfPool);
                }

                if (handles.Count == 0)
                {
                    return;
                }

                _returnHandles = handles.ToArray();
            }
        }

        internal sealed class GetCScriptResourceAddressTask : IScriptTask
        {
            #region Fields
            internal int _targetHandle;
            internal ulong* _poolAddress;
            internal int _elementSize;
            internal IntPtr _returnAddress;
            #endregion

            internal GetCScriptResourceAddressTask(int handle, ulong* poolAddress, int elementSize)
            {
                this._targetHandle = handle;
                this._poolAddress = poolAddress;
                this._elementSize = elementSize;
            }

            public void Run()
            {
                ulong cGameScriptHandlerAddress = s_getCGameScriptHandlerAddressFunc();

                if (cGameScriptHandlerAddress == 0)
                {
                    return;
                }

                CGameScriptResource* firstRegisteredScriptResourceItem = *(CGameScriptResource**)(cGameScriptHandlerAddress + 48);
                for (CGameScriptResource* item = firstRegisteredScriptResourceItem; item != null; item = item->next)
                {
                    if (item->counterOfPool != _targetHandle)
                    {
                        continue;
                    }

                    _returnAddress = new IntPtr((long)((byte*)(_poolAddress) + item->indexOfPool * _elementSize));
                    break;
                }
            }
        }

        internal sealed class GetCScriptResourceByIndexTask : IScriptTask
        {
            #region Fields
            internal CScriptResourceTypeNameIndex _resourceType;
            internal uint _targetIndex;
            internal CGameScriptResource* _result;
            #endregion

            internal GetCScriptResourceByIndexTask(CScriptResourceTypeNameIndex resourceType, uint index)
            {
                this._resourceType = resourceType;
                this._targetIndex = index;
            }

            public void Run()
            {
                ulong cGameScriptHandlerAddress = s_getCGameScriptHandlerAddressFunc();

                if (cGameScriptHandlerAddress == 0)
                {
                    return;
                }

                CGameScriptResource* firstItem = *(CGameScriptResource**)(cGameScriptHandlerAddress + 48);
                for (_result = firstItem;
                    _result != null && (_result->resourceTypeNameIndex != _resourceType || _result->indexOfPool != _targetIndex);
                    _result = _result->next
                    )
                {
                    ;
                }

                // _result should have the result address or null if not found
            }
        }

        #endregion

        #region -- Checkpoint Pool --

        private static ulong* s_checkpointPoolAddress;

        private static delegate* unmanaged[Stdcall]<ulong> s_getCGameScriptHandlerAddressFunc;

        public static int[] GetCheckpointHandles()
        {
            var task = new GetAllCScriptResourceHandlesTask(CScriptResourceTypeNameIndex.Checkpoint);

            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._returnHandles;
        }

        public static IntPtr GetCheckpointAddress(int handle)
        {
            var task = new GetCScriptResourceAddressTask(handle, s_checkpointPoolAddress, 0x60);

            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._returnAddress;
        }

        #endregion

        #region -- Scaleform Movie Data --

        public static bool IsScaleformMovieHandleValid(uint handle)
        {
            // handle cannot be zero
            if (handle == 0)
            {
                return false;
            }

            var task = new GetCScriptResourceByIndexTask(CScriptResourceTypeNameIndex.ScaleformMovie, handle);

            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._result != null;
        }

        #endregion

        #region -- Waypoint Info Array --

        private static ulong* s_waypointInfoArrayStartAddress;
        private static ulong* s_waypointInfoArrayEndAddress;
        private static ulong* s_waypointInfoArrayAddress1;
        private static ulong* s_waypointInfoArrayAddress2;
        private static ulong* s_waypointInfoArrayAddress3;
        private static ulong[] s_waypointInfoArrayAddresses = new ulong[4];
        private static delegate* unmanaged[Stdcall]<ulong> s_getLocalPlayerPedAddressFunc;

        public static int GetWaypointBlip()
        {
            // Enhanced effectively does the same, but the loop is unrolled and each element is accessed manually.
            // Just to be safe, we do the same thing here for Enhanced.

            if (s_isEnhanced)
            {
                foreach (ulong waypointInfoAddress in s_waypointInfoArrayAddresses)
                {
                    if (waypointInfoAddress == 0)
                    {
                        return 0;
                    }
                }
            }
            else
            {
                if (s_waypointInfoArrayStartAddress == null || s_waypointInfoArrayEndAddress == null)
                {
                    return 0;
                }
            }

            int playerPedModelHash = 0;
            ulong playerPedAddress = s_getLocalPlayerPedAddressFunc();

            if (playerPedAddress != 0)
            {
                playerPedModelHash = GetModelHashFromEntity(new IntPtr((long)playerPedAddress));
            }

            if (s_isEnhanced)
            {
                foreach (ulong waypointInfoAddress in s_waypointInfoArrayAddresses)
                {
                    int modelHash = *(int*)waypointInfoAddress;

                    if (modelHash == playerPedModelHash)
                    {
                        return *(int*)(waypointInfoAddress + 4);
                    }
                }
            }
            else
            {
                ulong waypointInfoAddress = (ulong)s_waypointInfoArrayStartAddress;
                for (; waypointInfoAddress < (ulong)s_waypointInfoArrayEndAddress; waypointInfoAddress += 0x18)
                {
                    int modelHash = *(int*)waypointInfoAddress;

                    if (modelHash == playerPedModelHash)
                    {
                        return *(int*)(waypointInfoAddress + 4);
                    }
                }
            }

            return 0;
        }

        #endregion

        #region -- Pool Addresses --

        private static delegate* unmanaged[Stdcall]<int, ulong> s_getPtfxAddressFunc;
        private static unsafe ulong* s_PtfxHashTableBuckets;
        private static unsafe short* s_PtfxHashTableCount;
        private static ulong s_PtfxVfuncSecondArgumentFuncAddr;
        private static delegate* unmanaged[Cdecl]<ulong, ulong, ulong, byte> s_isPtfxEntityUsableVFunc;
        private static ulong s_ptfxEntityVPtr;
        public static int PtfxBaseOffset { get; }
        public static int PtfxColorOffset { get; }
        public static int PtfxRangeOffset { get; }
        public static int PtfxScaleOffset { get; }
        // should be CGameScriptHandler::GetScriptEntity
        private static delegate* unmanaged[Stdcall]<int, ulong> s_getScriptEntity;




        public static IntPtr GetEntityAddress(int handle)
        {
            // In legacy, s_getScriptEntity contains the vfunc check (at 0x28).
            // However, This is not the case in Enhanced, and the check is usually inlined after it at the caller.
            // The vfunc usually takes the address returned by s_getScriptEntity (Enhanced) and 1 or 2 more arguments.
            // The second is usually a function pointer, and is not always the same.
            // In the cases where I used s_getScriptEntity (Enhanced), I implemented the check based on the caller.
            // Moving forward, I will either try to generalize it in this function, or keep implementing it after this call.
            return new IntPtr((long)s_getScriptEntity(handle));
        }

        public static IntPtr GetPtfxAddress(int handle)
        {
            if (s_isEnhanced)
            {
                // getPtfxAddressFunc was inlined in Enhanced, hence why we have to implement it ourselves.
                return GetPtfxAddressFunc(handle);
            }
            return new IntPtr((long)s_getPtfxAddressFunc(handle));
        }

        // Helpers for GetPtfxAddress:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool IsPtfxEntityUsable(ulong entity)
        {
            if (entity == 0)
            {
                return false;
            }

            try
            {
                // Cache VPtr and VFunc
                if (s_isPtfxEntityUsableVFunc == null || s_ptfxEntityVPtr == 0)
                {
                    s_ptfxEntityVPtr = *(ulong*)entity;

                    if (s_ptfxEntityVPtr == 0)
                    {
                        return false;
                    }

                    IntPtr funcPtr = new IntPtr(*(long*)(s_ptfxEntityVPtr + 0x28)); // TODO: find this offset dynamically.

                    if (funcPtr == IntPtr.Zero)
                    {
                        return false;
                    }
                    s_isPtfxEntityUsableVFunc = (delegate* unmanaged[Cdecl]<ulong, ulong, ulong, byte>)(funcPtr);
                }

                int result = s_isPtfxEntityUsableVFunc(entity, s_PtfxVfuncSecondArgumentFuncAddr, s_ptfxEntityVPtr);

                return result != 0;
            }
            catch (Exception)
            {
                //TODO: Add a reasonable Exception Class to throw in this case.
                return false;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe IntPtr GetPtfxAddressFunc(int handle)
        {
            ulong entity = s_getScriptEntity(handle);
            if (entity == 0 || !IsPtfxEntityUsable(entity))
            {
                return IntPtr.Zero;
            }

            if (*s_PtfxHashTableCount == 0 || *s_PtfxHashTableBuckets == 0)
                return IntPtr.Zero;

            uint keyed = (uint)(handle + 0x10000000);
            uint count = (uint)*s_PtfxHashTableCount;
            uint index = keyed % count;

            ulong* bucketsBase = (ulong*)*s_PtfxHashTableBuckets;

            ulong* node = (ulong*)bucketsBase[index];
            while (node != null)
            {
                ulong key = *(ulong*)node;
                if (key == keyed)
                {
                    ulong A = *(ulong*)(node + 1);
                    if (A == 0) return IntPtr.Zero;

                    return (IntPtr)A;
                }
                node = *(ulong**)(node + 2);
            }

            return IntPtr.Zero;
        }


        public static IntPtr GetBuildingAddress(int handle)
        {
            if (s_buildingPoolAddress == null)
            {
                return IntPtr.Zero;
            }

            return s_isEnhanced ? ((FwBasePoolEnhanced*)(NativeMemory.s_buildingPoolAddress))->GetAddressFromHandle(handle) : ((FwBasePoolLegacy*)(*NativeMemory.s_buildingPoolAddress))->GetAddressFromHandle(handle);
        }
        public static IntPtr GetAnimatedBuildingAddress(int handle)
        {
            if (s_animatedBuildingPoolAddress == null)
            {
                return IntPtr.Zero;
            }

            return s_isEnhanced ? ((FwBasePoolEnhanced*)(NativeMemory.s_animatedBuildingPoolAddress))->GetAddressFromHandle(handle) : ((FwBasePoolLegacy*)(*NativeMemory.s_animatedBuildingPoolAddress))->GetAddressFromHandle(handle);
        }
        public static IntPtr GetInteriorInstAddress(int handle)
        {
            if (s_interiorInstPoolAddress == null)
            {
                return IntPtr.Zero;
            }

            return s_isEnhanced ? ((FwBasePoolEnhanced*)(NativeMemory.s_interiorInstPoolAddress))->GetAddressFromHandle(handle) : ((FwBasePoolLegacy*)(*NativeMemory.s_interiorInstPoolAddress))->GetAddressFromHandle(handle);
        }
        public static IntPtr GetInteriorProxyAddress(int handle)
        {
            if (s_interiorProxyPoolAddress == null)
            {
                return IntPtr.Zero;
            }

            return s_isEnhanced ? ((FwBasePoolEnhanced*)(NativeMemory.s_interiorProxyPoolAddress))->GetAddressFromHandle(handle) : ((FwBasePoolLegacy*)(*NativeMemory.s_interiorProxyPoolAddress))->GetAddressFromHandle(handle);
        }

        #endregion

        #region  -- CObject Functions --

        // Although there are non-const variants of `GetAsProjectile*`, the vfuncs will use the same function in
        // final/production builds (with the function that returns the `this` argument and one that returns null/zero).
        private static int s_getAsCProjectileConstVFuncOffset;
        private static int s_getAsCProjectileRocketConstVFuncOffset;
        private static int s_getAsCProjectileThrownConstVFuncOffset;

        /// <summary>
        /// Returns the same address as the passed <c>CObject</c> address if the instance is a <c>CProjectile</c> or
        /// its subclass.
        /// </summary>
        /// <param name="cObjectAddress">The <c>CObject</c> address to test.</param>
        /// <returns>
        /// The same address as the passed <c>CObject</c> address if the instance is a <c>CProjectile</c> or
        /// its subclass; otherwise, <see cref="IntPtr.Zero"/>
        /// </returns>
        public static IntPtr GetAsCProjectile(IntPtr cObjectAddress)
        {
            if (s_getAsCProjectileConstVFuncOffset == 0)
            {
                return IntPtr.Zero;
            }

            ulong vFuncAddr = *(ulong*)(*(ulong*)cObjectAddress + (uint)s_getAsCProjectileConstVFuncOffset);
            var getAsCProjectileConstVFunc = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)(vFuncAddr);

            return getAsCProjectileConstVFunc(cObjectAddress);
        }
        /// <summary>
        /// Returns the same address as the passed <c>CObject</c> address if the instance is a <c>CProjectileRocket</c>.
        /// </summary>
        /// <param name="cObjectAddress">The <c>CObject</c> address to test.</param>
        /// <returns>
        /// The same address as the passed <c>CObject</c> address if the instance is a <c>CProjectileRocket</c>;
        /// otherwise, <see cref="IntPtr.Zero"/>
        /// </returns>
        public static IntPtr GetAsCProjectileRocket(IntPtr cObjectAddress)
        {
            if (s_getAsCProjectileRocketConstVFuncOffset == 0)
            {
                return IntPtr.Zero;
            }

            ulong vFuncAddr = *(ulong*)(*(ulong*)cObjectAddress + (uint)s_getAsCProjectileRocketConstVFuncOffset);
            var getAsCProjectileRocketConstVFunc = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)(vFuncAddr);

            return getAsCProjectileRocketConstVFunc(cObjectAddress);
        }
        /// <summary>
        /// Returns the same address as the passed <c>CObject</c> address if the instance is a <c>CProjectileRocket</c>.
        /// </summary>
        /// <param name="cObjectAddress">The <c>CObject</c> address to test.</param>
        /// <returns>
        /// The same address as the passed <c>CObject</c> address if the instance is a <c>CProjectileRocket</c>;
        /// otherwise, <see cref="IntPtr.Zero"/>
        /// </returns>
        public static IntPtr GetAsCProjectileThrown(IntPtr cObjectAddress)
        {
            if (s_getAsCProjectileThrownConstVFuncOffset == 0)
            {
                return IntPtr.Zero;
            }

            ulong vFuncAddr = *(ulong*)(*(ulong*)cObjectAddress + (uint)s_getAsCProjectileThrownConstVFuncOffset);
            var getAsCProjectileThrownConstVFunc = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)(vFuncAddr);

            return getAsCProjectileThrownConstVFunc(cObjectAddress);
        }

        public static int GetTargetEntityOfCProjectileRocket(IntPtr cProjectileRocketAddress)
        {
            if (ProjectileRocketTargetOffset == 0)
            {
                return 0;
            }

            var targetAddress = new IntPtr(*(long*)(cProjectileRocketAddress + ProjectileRocketTargetOffset));
            return targetAddress != IntPtr.Zero ? GetEntityHandleFromAddress(targetAddress) : 0;
        }

        #endregion

        #region -- Projectile Offsets --

        public static int ProjectileAmmoInfoOffset { get; }
        public static int ProjectileOwnerOffset { get; }

        #region -- Projectile Rocket Offsets --

        // `CProjectileRocket` has additional members and the layout of `CProjectileRocket` self hasn't changed
        // between b372 and b3095

        public static int ProjectileRocketCachedTargetPosOffset { get; }
        public static int ProjectileRocketLaunchDirOffset { get; }
        public static int ProjectileRocketTargetOffset { get; }

        // We should provide an option to access individual flight model inputs, as the yaw field may be in a different
        // cache line from one that the pitch and roll fields are in. `m_fPitch` and `m_fYaw` are at
        // [`CProjectileRocket` + 0x648] and [`CProjectileRocket` + 0x650] respectively but the first digits are
        // the same in all builds between b372 and b3095.
        public static int ProjectileRocketFlightModelInputPitchOffset { get; }
        public static int ProjectileRocketFlightModelInputRollOffset { get; }
        public static int ProjectileRocketFlightModelInputYawOffset { get; }

        /*
         * `ProjectileRocketSpeedOffset` would be inserted in this position for `CProjectileRocket::m_fSpeed`
         * but it is unused
         */

        public static int ProjectileRocketTimeBeforeHomingOffset { get; }
        public static int ProjectileRocketTimeBeforeHomingAngleBreakOffset { get; }
        public static int ProjectileRocketLauncherSpeedOffset { get; }
        public static int ProjectileRocketTimeSinceLaunchOffset { get; }

        /*
         * `ProjectileWhistleSoundAddressOffset` would be inserted for `audSound* m_pWhistleSound` here, but `audSound`
         * needs to be investigated before adding the member
         */

        /// <summary>
        /// The offset of the `<c>CProjectileRocket</c>` flags, which are consist of `<c>m_bIsAccurate</c>`,
        /// `<c>m_bLerpToLaunchDir</c>`, `<c>m_bApplyThrust</c>`, `<c>m_bOnFootHomingWeaponLockedOn</c>`,
        /// `<c>m_bWasHoming</c>`, `<c>m_bStopHoming</c>`, `<c>m_bHasBeenRedirected</c>`, and
        /// `<c>m_bTorpHasBeenOutOfWater</c>` (in said order).
        /// </summary>
        public static int ProjectileRocketFlagsOffset { get; }
        public static int ProjectileRocketCachedDirectionOffset { get; }

        #endregion

        #endregion

        #region -- Projectile Functions --

        private static delegate* unmanaged[Stdcall]<IntPtr, int, void> s_explodeProjectileFunc;

        public static void ExplodeProjectile(IntPtr projectileAddress)
        {
            var task = new ExplodeProjectileTask(projectileAddress);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);
        }

        internal sealed class ExplodeProjectileTask : IScriptTask
        {
            #region Fields
            internal IntPtr _projectileAddress;
            #endregion

            internal ExplodeProjectileTask(IntPtr projectileAddress)
            {
                this._projectileAddress = projectileAddress;
            }

            public void Run()
            {
                s_explodeProjectileFunc(_projectileAddress, 0);
            }
        }

        #endregion

        #region -- Interior Offsets --

        public static ulong* InteriorProxyPtrFromGameplayCamAddress { get; }
        public static int InteriorInstPtrInInteriorProxyOffset { get; }

        public static int GetAssociatedInteriorInstHandleFromInteriorProxy(int interiorProxyHandle)
        {
            if (InteriorInstPtrInInteriorProxyOffset == 0 || s_interiorInstPoolAddress == null)
            {
                return 0;
            }

            IntPtr interiorProxyAddress = GetInteriorProxyAddress(interiorProxyHandle);
            if (interiorProxyAddress == IntPtr.Zero)
            {
                return 0;
            }

            ulong interiorInstAddress = *(ulong*)(interiorProxyAddress + InteriorInstPtrInInteriorProxyOffset).ToPointer();
            if (interiorInstAddress == 0)
            {
                return 0;
            }

            return s_isEnhanced ? ((FwBasePoolEnhanced*)(NativeMemory.s_interiorInstPoolAddress))->GetGuidHandleFromAddress(interiorInstAddress) : ((FwBasePoolLegacy*)(*NativeMemory.s_interiorInstPoolAddress))->GetGuidHandleFromAddress(interiorInstAddress);
        }
        public static int GetInteriorProxyHandleFromInteriorInst(int interiorInstHandle)
        {
            if (s_interiorProxyPoolAddress == null)
            {
                return 0;
            }

            IntPtr interiorInstAddress = GetInteriorInstAddress(interiorInstHandle);
            if (interiorInstAddress == IntPtr.Zero)
            {
                return 0;
            }

            ulong interiorProxyAddress = *(ulong*)(interiorInstAddress + 0x188).ToPointer();
            if (interiorProxyAddress == 0)
            {
                return 0;
            }

            return s_isEnhanced ? ((FwBasePoolEnhanced*)(NativeMemory.s_interiorProxyPoolAddress))->GetGuidHandleFromAddress(interiorProxyAddress) : ((FwBasePoolLegacy*)(*NativeMemory.s_interiorProxyPoolAddress))->GetGuidHandleFromAddress(interiorProxyAddress);
        }
        public static int GetInteriorProxyHandleFromGameplayCam()
        {
            if (InteriorProxyPtrFromGameplayCamAddress == null || s_interiorInstPoolAddress == null)
            {
                return 0;
            }

            ulong interiorProxyAddress = *InteriorProxyPtrFromGameplayCamAddress;
            if (interiorProxyAddress == 0)
            {
                return 0;
            }

            return s_isEnhanced ? ((FwBasePoolEnhanced*)(NativeMemory.s_interiorProxyPoolAddress))->GetGuidHandleFromAddress(interiorProxyAddress) : ((FwBasePoolLegacy*)(*NativeMemory.s_interiorProxyPoolAddress))->GetGuidHandleFromAddress(interiorProxyAddress);
        }

        public static int GetEntityHandleFromAddress(IntPtr address)
        {
            var task = new GetEntityHandleTask(address);

            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._returnEntityHandle;
        }

        private static int GetBuildingHandleFromAddress(IntPtr address)
        {
            if (s_buildingPoolAddress == null)
            {
                return 0;
            }

            return GetHandleForFwBasePoolFromAddress(s_buildingPoolAddress, address);
        }

        private static int GetHandleForFwBasePoolFromAddress(ulong* poolAddress, IntPtr instanceAddress) => s_isEnhanced? ((FwBasePoolEnhanced*)poolAddress)->GetGuidHandleFromAddress((ulong)instanceAddress) : ((FwBasePoolLegacy*)*poolAddress)->GetGuidHandleFromAddress((ulong)instanceAddress);

        #endregion

        #region -- Weapon Info And Ammo Info --

        // TODO: support size and capacity type other than ushort (uint16_t).
        // Actually defined like rage::atArray<element_type, some_size, size_and_capacity_type> in the exe, but we
        // assumed the template was defined with 1 type parameter before the big incident happened.

        // Same layout in Enhanced.
        [StructLayout(LayoutKind.Explicit, Size = 0x10)]
        public struct RageAtArrayPtr
        {
            [FieldOffset(0x0)]
            public ulong* data;
            [FieldOffset(0x8)]
            public ushort size;
            [FieldOffset(0xA)]
            public ushort capacity;
            // rage::atArray always has 4-byte padding at the end
            [FieldOffset(0xC)]
            private fixed char padding[4];

            public ulong GetElementAddress(int i)
            {
                return data[i];
            }
        }

        private static RageAtArrayPtr* s_weaponAndAmmoInfoArrayPtr;

        private static HashSet<uint> s_disallowWeaponHashSetForHumanPedsOnFoot = new HashSet<uint>()
        {
            0x1B79F17,  /* weapon_briefcase_02 */
            0x166218FF, /* weapon_passenger_rocket */
            0x32A888BD, /* weapon_tranquilizer */
            0x687652CE, /* weapon_stinger */
            0x6D5E2801, /* weapon_bird_crap */
            0x88C78EB7, /* weapon_briefcase */
            0xFDBADCED, /* weapon_digiscanner */
        };

        private static uint* s_weaponComponentArrayCountAddr;
        // Store the offset instead of the calculated address for compatibility with mods like Weapon Limits Adjuster by alexguirre (although Weapon Limits Adjuster allocates a new array in the very beginning).
        private static ulong s_offsetForCWeaponComponentArrayAddr;
        private static int s_weaponAttachPointsStartOffset;
        private static int s_weaponAttachPointsArrayCountOffset;
        private static int s_weaponAttachPointElementComponentCountOffset;
        private static int s_weaponAttachPointElementSize;

        private static int s_weaponInfoHumanNameHashOffset;

        [StructLayout(LayoutKind.Explicit, Size = 0x20)]
        public struct ItemInfo
        {
            [FieldOffset(0x0)]
            public ulong* vTable;
            [FieldOffset(0x10)]
            public uint nameHash;
            [FieldOffset(0x14)]
            public uint modelHash;
            [FieldOffset(0x18)]
            public uint audioHash;
            [FieldOffset(0x1C)]
            public uint slot;

            public uint GetClassNameHash()
            {
                // In the b2802 or a later exe, the function returns a hash value (not a pointer value)
                if (s_isEnhanced || GetGameVersion() >= 80)
                {
                    // The function is for the game version b2802 or later ones.
                    // This one directly returns a hash value (not a pointer value) unlike the previous function.
                    var getClassNameHashFunc = (delegate* unmanaged[Stdcall]<uint>)(vTable[2]);
                    return getClassNameHashFunc();
                }

                // The function is for game versions prior to b2802.
                // The function uses rax and rdx registers in newer versions prior to b2802 (probably since b2189), and it uses only rax register in older versions.
                // The function returns the address where the class name hash is in all versions prior to (the address will be the outVal address in newer versions).
                var getClassNameAddressHashFunc = (delegate* unmanaged[Stdcall]<ulong, uint*, uint*>)(vTable[2]);

                uint outVal = 0;
                uint* returnValueAddress = getClassNameAddressHashFunc(0, &outVal);
                return *returnValueAddress;
            }
        }

        [StructLayout(LayoutKind.Explicit, Size = 0x48)]
        private struct WeaponComponentInfo
        {
            [FieldOffset(0x0)]
            internal ulong* vTable;
            [FieldOffset(0x10)]
            internal uint nameHash;
            [FieldOffset(0x14)]
            internal uint modelHash;
            [FieldOffset(0x18)]
            internal uint locNameHash;
            [FieldOffset(0x1C)]
            internal uint locDescHash;
            [FieldOffset(0x40)]
            internal bool shownOnWheel;
            [FieldOffset(0x41)]
            internal bool createObject;
            [FieldOffset(0x42)]
            internal bool applyWeaponTint;
        }


        /// <summary>
        /// Represents a `<c>CWeaponComponentPoint</c>` but without the `<c>m_Components</c>` field followed by
        /// `<c>m_AttachBoneId</c>`, where the type is <c>atFixedArray&lt;sComponent, MAX_WEAPON_COMPONENTS&gt;</c> and
        /// `<c>MAX_WEAPON_COMPONENTS</c>` is a hardcoded `<c>i32</c>`/`<c>s32</c>` const.
        /// </summary>
        /// <remarks>
        /// This struct omits the field for `<c>m_Components</c>` because its byte size can be grown in some game
        /// updates (e.g. `<c>m_AttachBoneId</c>` takes 0x5C bytes in b2699 and takes 0x64 bytes in b3095).
        /// </remarks>
        [StructLayout(LayoutKind.Explicit, Size = 0x8)]
        private struct WeaponComponentPointHeader
        {
            /// <summary>
            /// The attach bone hash for the `<c>m_AttachBone</c>` field, where the type is
            /// `<c>atHashWithStringNotFinal</c>` (basically just a `<c>u32</c>` hash).
            /// </summary>
            [FieldOffset(0x0)]
            internal uint AttachBoneHash;
            /// <summary>
            /// The corresponding bone hierarchy id (index) for the attach bone for the `<c>m_AttachBoneId</c>` field,
            /// where the type is `<c>eHierarchyId</c>` (a `<c>i32</c>`/`<c>s32</c>` enum).
            /// </summary>
            [FieldOffset(0x4)]
            internal uint AttachBoneId;
        }

        private static ItemInfo* FindItemInfoFromWeaponAndAmmoInfoArray(uint nameHash)
        {
            if (s_weaponAndAmmoInfoArrayPtr == null)
            {
                return null;
            }

            ushort weaponAndAmmoInfoElementCount = s_weaponAndAmmoInfoArrayPtr->size;

            if (weaponAndAmmoInfoElementCount == 0)
            {
                return null;
            }

            int low = 0, high = weaponAndAmmoInfoElementCount - 1;
            while (true)
            {
                int indexToRead = (low + high) >> 1;
                var weaponOrAmmoInfo = (ItemInfo*)s_weaponAndAmmoInfoArrayPtr->GetElementAddress(indexToRead);

                if (weaponOrAmmoInfo->nameHash == nameHash)
                {
                    return weaponOrAmmoInfo;
                }

                // The array is sorted in ascending order
                if (weaponOrAmmoInfo->nameHash <= nameHash)
                {
                    low = indexToRead + 1;
                }
                else
                {
                    high = indexToRead - 1;
                }

                if (low > high)
                {
                    return null;
                }
            }
        }

        private static ItemInfo* FindWeaponInfo(uint nameHash)
        {
            ItemInfo* itemInfoPtr = FindItemInfoFromWeaponAndAmmoInfoArray(nameHash);

            if (itemInfoPtr == null)
            {
                return null;
            }

            uint classNameHash = itemInfoPtr->GetClassNameHash();

            const uint cWeaponInfoNameHash = 0x861905B4;
            if (classNameHash == cWeaponInfoNameHash)
            {
                return itemInfoPtr;
            }

            return null;
        }

        private static WeaponComponentInfo* FindWeaponComponentInfo(uint nameHash)
        {
            ulong* cWeaponComponentArrayFirstPtr = (ulong*)((byte*)s_offsetForCWeaponComponentArrayAddr + 4 + *(int*)s_offsetForCWeaponComponentArrayAddr);
            uint arrayCount = s_weaponComponentArrayCountAddr != null ? *(uint*)s_weaponComponentArrayCountAddr : 0;
            if (cWeaponComponentArrayFirstPtr == null || arrayCount == 0)
            {
                return null;
            }

            int low = 0, high = (int)arrayCount - 1;
            while (true)
            {
                int indexToRead = (low + high) >> 1;
                var weaponComponentInfo = (WeaponComponentInfo*)cWeaponComponentArrayFirstPtr[indexToRead];

                if (weaponComponentInfo->nameHash == nameHash)
                {
                    return weaponComponentInfo;
                }

                // The array is sorted in ascending order
                if (weaponComponentInfo->nameHash <= nameHash)
                {
                    low = indexToRead + 1;
                }
                else
                {
                    high = indexToRead - 1;
                }

                if (low > high)
                {
                    return null;
                }
            }
        }

        public static bool IsHashValidAsWeaponHash(uint weaponHash) => FindWeaponInfo(weaponHash) != null;

        public static uint GetAttachmentPointHash(uint weaponHash, uint componentHash)
        {
            ItemInfo* weaponInfo = FindWeaponInfo(weaponHash);

            if (weaponInfo == null)
            {
                return 0xFFFFFFFF;
            }

            byte* weaponAttachPointsAddr = (byte*)weaponInfo + s_weaponAttachPointsStartOffset;
            int weaponAttachPointsCount = *(int*)(weaponAttachPointsAddr + s_weaponAttachPointsArrayCountOffset);
            byte* weaponAttachPointElementStartAddr = (byte*)(weaponAttachPointsAddr);

            for (int i = 0; i < weaponAttachPointsCount; i++)
            {
                byte* weaponAttachPointElementAddr = weaponAttachPointElementStartAddr + (i * s_weaponAttachPointElementSize) + 0x8;
                int componentItemsCount = *(int*)(weaponAttachPointElementAddr + s_weaponAttachPointElementComponentCountOffset);

                if (componentItemsCount <= 0)
                {
                    continue;
                }

                for (int j = 0; j < componentItemsCount; j++)
                {
                    uint componentHashInItemArray = *(uint*)(weaponAttachPointElementAddr + j * 0x8);
                    if (componentHashInItemArray == componentHash)
                    {
                        return ((WeaponComponentPointHeader*)(weaponAttachPointElementStartAddr + i * s_weaponAttachPointElementSize))->AttachBoneHash;
                    }
                }
            }

            return 0xFFFFFFFF;
        }

        public static List<uint> GetAllWeaponHashesForHumanPeds()
        {
            if (s_weaponAndAmmoInfoArrayPtr == null)
            {
                return new List<uint>();
            }

            ushort weaponAndAmmoInfoElementCount = s_weaponAndAmmoInfoArrayPtr->size;
            var resultList = new List<uint>();

            for (int i = 0; i < weaponAndAmmoInfoElementCount; i++)
            {
                var weaponOrAmmoInfo = (ItemInfo*)s_weaponAndAmmoInfoArrayPtr->GetElementAddress(i);

                if (!CanPedEquip(weaponOrAmmoInfo) || s_disallowWeaponHashSetForHumanPedsOnFoot.Contains(weaponOrAmmoInfo->nameHash))
                {
                    continue;
                }

                uint classNameHash = weaponOrAmmoInfo->GetClassNameHash();

                const uint cWeaponInfoNameHash = 0x861905B4;
                if (classNameHash == cWeaponInfoNameHash)
                {
                    resultList.Add(weaponOrAmmoInfo->nameHash);
                }
            }

            return resultList;

            bool CanPedEquip(ItemInfo* weaponInfoAddress)
            {
                return weaponInfoAddress->modelHash != 0 && weaponInfoAddress->slot != 0;
            }
        }

        public static List<uint> GetAllWeaponComponentHashes()
        {
            ulong* cWeaponComponentArrayFirstPtr = (ulong*)((byte*)s_offsetForCWeaponComponentArrayAddr + 4 + *(int*)s_offsetForCWeaponComponentArrayAddr);
            uint arrayCount = s_weaponComponentArrayCountAddr != null ? *(uint*)s_weaponComponentArrayCountAddr : 0;
            var resultList = new List<uint>();

            for (uint i = 0; i < arrayCount; i++)
            {
                ulong cWeaponComponentInfo = cWeaponComponentArrayFirstPtr[i];
                uint weaponComponentNameHash = *(uint*)(cWeaponComponentInfo + 0x10);
                resultList.Add(weaponComponentNameHash);
            }

            return resultList;
        }

        public static List<uint> GetAllCompatibleWeaponComponentHashes(uint weaponHash)
        {
            ItemInfo* weaponInfo = FindWeaponInfo(weaponHash);

            if (weaponInfo == null)
            {
                return new List<uint>();
            }

            var returnList = new List<uint>();

            byte* weaponAttachPointsAddr = (byte*)weaponInfo + s_weaponAttachPointsStartOffset;
            int weaponAttachPointsCount = *(int*)(weaponAttachPointsAddr + s_weaponAttachPointsArrayCountOffset);
            byte* weaponAttachPointElementStartAddr = (byte*)(weaponAttachPointsAddr + 0x8);
            for (int i = 0; i < weaponAttachPointsCount; i++)
            {
                byte* weaponAttachPointElementAddr = weaponAttachPointElementStartAddr + i * s_weaponAttachPointElementSize;
                int componentItemsCount = *(int*)(weaponAttachPointElementAddr + s_weaponAttachPointElementComponentCountOffset);

                if (componentItemsCount <= 0)
                {
                    continue;
                }

                for (int j = 0; j < componentItemsCount; j++)
                {
                    returnList.Add(*(uint*)(weaponAttachPointElementAddr + j * 0x8));
                }
            }

            return returnList;
        }

        public static uint GetHumanNameHashOfWeaponInfo(uint weaponHash)
        {
            ItemInfo* weaponInfo = FindWeaponInfo(weaponHash);

            if (weaponInfo == null)
                // hashed value of WT_INVALID
            {
                return 0xBFED8500;
            }

            return *(uint*)((byte*)weaponInfo + s_weaponInfoHumanNameHashOffset);
        }

        public static uint GetHumanNameHashOfWeaponComponentInfo(uint weaponComponentHash)
        {
            WeaponComponentInfo* weaponComponentInfo = FindWeaponComponentInfo(weaponComponentHash);

            if (weaponComponentInfo == null)
                // hashed value of WCT_INVALID
            {
                return 0xDE4BE9F8;
            }

            return weaponComponentInfo->locNameHash;
        }

        #endregion

        #region -- Fragment Object for Entity --

        private static int s_getFragInstVFuncOffset;
        private static delegate* unmanaged[Stdcall]<FragInst*, int, FragInst*> s_detachFragmentPartByIndexFunc;
        private static ulong** s_phSimulatorInstPtr;
        private static int s_colliderCapacityOffset;
        private static int s_colliderCountOffset;

        [StructLayout(LayoutKind.Explicit, Size = 0xC0)]
        internal unsafe struct FragInst
        {
            [FieldOffset(0x68)]
            internal FragCacheEntry* fragCacheEntry;
            [FieldOffset(0x78)]
            internal GtaFragType* gtaFragType;
            [FieldOffset(0xB8)]
            internal uint unkType;

            internal FragPhysicsLod* GetAppropriateFragPhysicsLod()
            {
                FragPhysicsLodGroup* fragPhysicsLodGroup = gtaFragType->fragPhysicsLODGroup;
                if (fragPhysicsLodGroup == null)
                {
                    return null;
                }

                switch (unkType)
                {
                    case 0:
                    case 1:
                    case 2:
                        return fragPhysicsLodGroup->GetFragPhysicsLodByIndex((int)unkType);
                    default:
                        return fragPhysicsLodGroup->GetFragPhysicsLodByIndex(0);
                }
            }
        }
        [StructLayout(LayoutKind.Explicit)]
        internal unsafe struct FragCacheEntry
        {
            [FieldOffset(0x178)] internal CrSkeleton* crSkeleton;
        }
        [StructLayout(LayoutKind.Explicit)]
        internal struct GtaFragType
        {
            [FieldOffset(0x30)]
            internal FragDrawable* fragDrawable;
            [FieldOffset(0xF0)]
            internal FragPhysicsLodGroup* fragPhysicsLODGroup;
        }
        [StructLayout(LayoutKind.Explicit)]
        internal struct FragDrawable
        {
            [FieldOffset(0x18)]
            internal CrSkeletonData* crSkeletonData;
        }
        [StructLayout(LayoutKind.Explicit)]
        internal struct FragPhysicsLodGroup
        {
            [FieldOffset(0x10)]
            internal fixed ulong fragPhysicsLODAddresses[3];

            internal FragPhysicsLod* GetFragPhysicsLodByIndex(int index) => (FragPhysicsLod*)((ulong*)fragPhysicsLODAddresses[index]);
        }
        [StructLayout(LayoutKind.Explicit)]
        internal struct FragPhysicsLod
        {
            [FieldOffset(0xD0)]
            internal ulong fragTypeChildArr;
            [FieldOffset(0x11E)]
            internal byte fragmentGroupCount;

            internal FragTypeChild* GetFragTypeChild(int index)
            {
                if (index >= fragmentGroupCount)
                {
                    return null;
                }

                return (FragTypeChild*)*((ulong*)fragTypeChildArr + index);
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct FragTypeChild
        {
            [FieldOffset(0x10)]
            internal ushort boneIndex;
            [FieldOffset(0x12)]
            internal ushort boneId;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal unsafe struct CrSkeleton
        {
            [FieldOffset(0x00)] internal CrSkeletonData* skeletonData;
            // this field has a pointer to one matrix, not a pointer to an array of matrices for all bones
            [FieldOffset(0x8)] internal ulong boneTransformMatrixPtr;
            // object matrices (entity-local space)
            [FieldOffset(0x10)] internal ulong boneObjectMatrixArrayPtr;
            // global matrices (world space)
            [FieldOffset(0x18)] internal ulong boneGlobalMatrixArrayPtr;
            [FieldOffset(0x20)] internal int boneCount;

            public IntPtr GetTransformMatrixAddress()
            {
                return new IntPtr((long)(boneTransformMatrixPtr));
            }

            public IntPtr GetBoneObjectMatrixAddress(int boneIndex)
            {
                return new IntPtr((long)(boneObjectMatrixArrayPtr + ((uint)boneIndex * 0x40)));
            }

            public IntPtr GetBoneGlobalMatrixAddress(int boneIndex)
            {
                return new IntPtr((long)(boneGlobalMatrixArrayPtr + ((uint)boneIndex * 0x40)));
            }
        }

        [StructLayout(LayoutKind.Explicit, Size = 0x50)]
        internal struct CrBoneData
        {
            // Rotation (quaternion) is between 0x0 - 0x10
            // Translation (vector3) is between 0x10 - 0x1C
            // Scale (vector3?) is between 0x20 - 0x2C
            [FieldOffset(0x30)]
            internal ushort nextSiblingBoneIndex;
            [FieldOffset(0x32)]
            internal ushort parentBoneIndex;
            [FieldOffset(0x38)]
            internal IntPtr namePtr;
            [FieldOffset(0x42)]
            internal ushort boneIndex;
            [FieldOffset(0x44)]
            internal ushort boneId;

            internal string Name => namePtr == default ? null : Marshal.PtrToStringAnsi(namePtr);
        }

        [StructLayout(LayoutKind.Explicit)]
        internal unsafe struct CrSkeletonData
        {
            [FieldOffset(0x10)] internal PgHashMap boneHashMap;
            [FieldOffset(0x20)] internal CrBoneData* boneData;
            [FieldOffset(0x5E)] internal ushort boneCount;

            /// <summary>
            /// Gets the bone index from specified bone id. Note that bone indexes are sequential values and bone ids are not sequential ones.
            /// </summary>
            public int GetBoneIndexByBoneId(int boneId)
            {
                if (boneHashMap.elementCount == 0)
                {
                    if (boneId < boneCount)
                    {
                        return boneId;
                    }

                    return -1;
                }

                if (boneHashMap.bucketCount == 0)
                {
                    return -1;
                }

                if (boneHashMap.Get((uint)boneId, out int returnBoneId))
                {
                    return returnBoneId;
                }

                return -1;
            }

            /// <summary>
            /// Gets the bone id from specified bone index. Note that bone indexes are sequential values and bone ids are not sequential ones.
            /// </summary>
            internal int GetBoneIdByIndex(int boneIndex)
            {
                if (boneIndex < 0 || boneIndex >= boneCount)
                {
                    return -1;
                }

                return ((CrBoneData*)((ulong)boneData + (uint)sizeof(CrBoneData) * (uint)boneIndex))->boneId;
            }

            /// <summary>
            /// Gets the next sibling bone index of specified bone index.
            /// </summary>
            internal void GetNextSiblingBoneIndexAndId(int boneIndex, out int nextSiblingBoneIndex, out int nextSiblingBoneId)
            {
                if (boneIndex < 0 || boneIndex >= boneCount)
                {
                    nextSiblingBoneIndex = -1;
                    nextSiblingBoneId = -1;
                    return;
                }

                var crBoneData = ((CrBoneData*)((ulong)boneData + (uint)sizeof(CrBoneData) * (uint)boneIndex));
                ushort nextSiblingBoneIndexFetched = crBoneData->nextSiblingBoneIndex;
                if (nextSiblingBoneIndexFetched == 0xFFFF)
                {
                    nextSiblingBoneIndex = -1;
                    nextSiblingBoneId = -1;
                    return;
                }

                int nextSiblingBoneIdFetched = GetBoneIdByIndex(nextSiblingBoneIndexFetched);
                if (nextSiblingBoneIndexFetched == 0xFFFF)
                {
                    nextSiblingBoneIndex = -1;
                    nextSiblingBoneId = -1;
                    return;
                }

                nextSiblingBoneIndex = nextSiblingBoneIndexFetched;
                nextSiblingBoneId = nextSiblingBoneIdFetched;
            }

            /// <summary>
            /// Gets the next parent bone index of specified bone index.
            /// </summary>
            internal void GetParentBoneIndexAndId(int boneIndex, out int parentBoneIndex, out int parentBoneId)
            {
                if (boneIndex < 0 || boneIndex >= boneCount)
                {
                    parentBoneIndex = -1;
                    parentBoneId = -1;
                    return;
                }

                var crBoneData = ((CrBoneData*)((ulong)boneData + (uint)sizeof(CrBoneData) * (uint)boneIndex));
                ushort nextParentBoneIndexFetched = crBoneData->parentBoneIndex;
                if (nextParentBoneIndexFetched == 0xFFFF)
                {
                    parentBoneIndex = -1;
                    parentBoneId = -1;
                    return;
                }

                int nextParentBoneIdFetched = GetBoneIdByIndex(nextParentBoneIndexFetched);
                if (nextParentBoneIdFetched == 0xFFFF)
                {
                    parentBoneIndex = -1;
                    parentBoneId = -1;
                    return;
                }

                parentBoneIndex = nextParentBoneIndexFetched;
                parentBoneId = nextParentBoneIdFetched;
            }

            /// <summary>
            /// Gets the bone name string from specified bone index.
            /// </summary>
            internal string GetBoneName(int boneIndex)
            {
                if (boneIndex < 0 || boneIndex >= boneCount)
                {
                    return null;
                }

                return ((CrBoneData*)((ulong)boneData + (uint)sizeof(CrBoneData) * (uint)boneIndex))->Name;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct HashEntry
        {
            internal uint hash;
            internal int data;
            internal HashEntry* next;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal unsafe struct PgHashMap
        {
            [FieldOffset(0x0)]
            internal ulong* buckets;
            [FieldOffset(0x8)]
            internal ushort bucketCount;
            [FieldOffset(0xA)]
            internal ushort elementCount;

            internal ulong GetBucketAddress(int index)
            {
                return buckets[index];
            }

            internal bool Get(uint hash, out int value)
            {
                ulong* firstEntryAddr = (ulong*)GetBucketAddress((int)(hash % bucketCount));
                for (var hashEntry = (HashEntry*)firstEntryAddr; hashEntry != null; hashEntry = hashEntry->next)
                {
                    if (hash != hashEntry->hash)
                    {
                        continue;
                    }

                    value = hashEntry->data;
                    return true;
                }

                value = default;
                return false;
            }
        }

        internal sealed class DetachFragmentPartByIndexTask : IScriptTask
        {
            #region Fields
            internal FragInst* _fragInst;
            internal int _fragmentGroupIndex;
            internal bool _wasNewFragInstCreated;
            #endregion

            internal DetachFragmentPartByIndexTask(FragInst* fragInst, int fragmentGroupIndex)
            {
                this._fragInst = fragInst;
                this._fragmentGroupIndex = fragmentGroupIndex;
            }

            public void Run()
            {
                _wasNewFragInstCreated = s_detachFragmentPartByIndexFunc(_fragInst, _fragmentGroupIndex) != null;
            }
        }

        public static int GetFragmentGroupCountFromEntity(IntPtr entityAddress)
        {
            FragInst* fragInst = GetFragInstAddressOfEntity(entityAddress);
            if (fragInst == null)
            {
                return 0;
            }

            return GetFragmentGroupCountOfFragInst(fragInst);
        }

        public static bool DetachFragmentPartByIndex(IntPtr entityAddress, int fragmentGroupIndex)
        {
            if (fragmentGroupIndex < 0)
            {
                return false;
            }

            // If the entity collider count is at the capacity, the game can crash for trying to create the new entity while no free collider slots are available
            if (GetEntityColliderCount() >= GetEntityColliderCapacity())
            {
                return false;
            }

            FragInst* fragInst = GetFragInstAddressOfEntity(entityAddress);
            if (fragInst == null)
            {
                return false;
            }

            int fragmentGroupCount = GetFragmentGroupCountOfFragInst(fragInst);
            if (fragmentGroupIndex >= fragmentGroupCount)
            {
                return false;
            }

            var task = new DetachFragmentPartByIndexTask(fragInst, fragmentGroupIndex);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);

            return task._wasNewFragInstCreated;
        }

        public static int GetFragmentGroupIndexByEntityBoneIndex(IntPtr entityAddress, int boneIndex)
        {
            if ((boneIndex & 0x80000000) != 0) // boneIndex cant be negative
            {
                return -1;
            }

            FragInst* fragInst = GetFragInstAddressOfEntity(entityAddress);
            if (fragInst == null)
            {
                return -1;
            }


            CrSkeletonData* crSkeletonData = fragInst->gtaFragType->fragDrawable->crSkeletonData;
            if (crSkeletonData == null)
            {
                return -1;
            }

            ushort boneCount = crSkeletonData->boneCount;
            if (boneIndex >= boneCount)
            {
                return -1;
            }

            FragPhysicsLod* fragPhysicsLod = fragInst->GetAppropriateFragPhysicsLod();
            if (fragPhysicsLod == null)
            {
                return -1;
            }

            byte fragmentGroupCount = fragPhysicsLod->fragmentGroupCount;

            for (int i = 0; i < fragmentGroupCount; i++)
            {
                FragTypeChild* fragTypeChild = fragPhysicsLod->GetFragTypeChild(i);

                if (fragTypeChild == null)
                {
                    continue;
                }

                if (boneIndex == crSkeletonData->GetBoneIndexByBoneId(fragTypeChild->boneId))
                {
                    return i;
                }
            }

            return -1;
        }

        public static int GetEntityColliderCapacity()
        {
            if (*s_phSimulatorInstPtr == null)
            {
                return 0;
            }

            return *(int*)((byte*)*s_phSimulatorInstPtr + s_colliderCapacityOffset);
        }

        public static int GetEntityColliderCount()
        {
            if (*s_phSimulatorInstPtr == null)
            {
                return 0;
            }

            return *(int*)((byte*)*s_phSimulatorInstPtr + s_colliderCountOffset);
        }

        public static bool IsEntityFragmentObject(IntPtr entityAddress)
        {
            // For CObject, a valid address will be returned only when a certain flag is set. For CPed and CVehicle, a valid address will always be returned.
            return GetFragInstAddressOfEntity(entityAddress) != null;
        }

        private static FragInst* GetFragInstAddressOfEntity(IntPtr entityAddress)
        {
            ulong vFuncAddr = *(ulong*)(*(ulong*)entityAddress.ToPointer() + (uint)s_getFragInstVFuncOffset);
            var getFragInstFunc = (delegate* unmanaged[Stdcall]<IntPtr, FragInst*>)(vFuncAddr);

            return getFragInstFunc(entityAddress);
        }

        /// <summary>
        /// Gets whether the passed entity has a skeleton.
        /// This method is provided so it makes possible for SHVDN to avoid the game crashing for trying to use an absent
        /// CrSkeleton address when calling entity attachment native functions with bone indices but one of them does not
        /// have a CrSkeleton, even in the game versions earlier than v1.0.2699.0.
        /// </summary>
        public static bool EntityHasSkeleton(int handle)
        {
            IntPtr addr = GetEntityAddress(handle);
            if (addr == null)
            {
                return false;
            }

            return CEntityHasCrSkeleton(addr);
        }
        /// <summary>
        /// Gets whether the CEntity has a CrSkeleton.
        /// Basically does the same thing as what the native DOES_ENTITY_HAVE_SKELETON does but this method takes
        /// an CEntity address.
        /// </summary>
        /// <param name="cEntityAddress">The CEntity address (does not has to be CPhysical).</param>
        public static bool CEntityHasCrSkeleton(IntPtr cEntityAddress)
        {
            FragInst* fragInst = GetFragInstAddressOfEntity(cEntityAddress);
            if (fragInst == null)
            {
                return false;
            }

            FragCacheEntry* fragCache = fragInst->fragCacheEntry;
            if (fragCache == null)
            {
                return false;
            }

            CrSkeleton* crSkel = fragCache->crSkeleton;
            if (crSkel == null)
            {
                return false;
            }

            return true;
        }

        private static int GetFragmentGroupCountOfFragInst(FragInst* fragInst)
        {
            FragPhysicsLod* fragPhysicsLod = fragInst->GetAppropriateFragPhysicsLod();
            return fragPhysicsLod != null ? fragPhysicsLod->fragmentGroupCount : 0;
        }


        #endregion

        #region -- NaturalMotion Euphoria --

        // These CNmParameter functions can also be called as virtual functions for your information
        private static delegate* unmanaged[Stdcall]<ulong, IntPtr, int, byte> s_setNmParameterInt;
        private static delegate* unmanaged[Stdcall]<ulong, IntPtr, bool, byte> s_setNmParameterBool;
        private static delegate* unmanaged[Stdcall]<ulong, IntPtr, float, byte> s_setNmParameterFloat;
        private static delegate* unmanaged[Stdcall]<ulong, IntPtr, IntPtr, byte> s_setNmParameterString;
        private static delegate* unmanaged[Stdcall]<ulong, IntPtr, float, float, float, byte> s_setNmParameterVector;

        private static delegate* unmanaged[Stdcall]<ulong, ulong, int, ulong> s_initMessageMemoryFunc;
        private static delegate* unmanaged[Stdcall]<ulong, IntPtr, ulong, void> s_sendNmMessageToPedFunc;
        private static delegate* unmanaged[Stdcall]<ulong, CTask*> s_getActiveTaskFunc;

        private static int s_fragInstNmGtaOffset;
        private static int s_cTaskNmScriptControlTypeIndex;
        private static int s_cEventSwitch2NmTypeIndex;
        private static uint s_getEventTypeIndexVFuncOffset;
        private static uint s_fragInstNmGtaGetUnkValVFuncOffset;

        // Same struct in Enhanced.
        [StructLayout(LayoutKind.Explicit, Size = 0x38)]
        private struct CTask
        {
            [FieldOffset(0x34)]
            internal ushort taskTypeIndex;
        }

        public static bool IsTaskNmScriptControlOrEventSwitch2NmActive(IntPtr pedAddress)
        {
            ulong phInstGtaAddress = *(ulong*)(pedAddress + 0x30);

            if (phInstGtaAddress == 0)
            {
                return false;
            }

            ulong fragInstNmGtaAddress = *(ulong*)(pedAddress + s_fragInstNmGtaOffset);

            if (phInstGtaAddress != fragInstNmGtaAddress || IsPedInjured((byte*)pedAddress))
            {
                return false;
            }

            // This virtual function will return -1 if phInstGta is not a NM one
            var fragInstNmGtaGetUnkValVFunc = (delegate* unmanaged[Stdcall]<ulong, int>)(new IntPtr((long)*(ulong*)(*(ulong*)fragInstNmGtaAddress + s_fragInstNmGtaGetUnkValVFuncOffset)));
            if (fragInstNmGtaGetUnkValVFunc(fragInstNmGtaAddress) == -1)
            {
                return false;
            }

            ulong pedIntelligenceAddr = *(ulong*)(pedAddress + Ped.PedIntelligenceOffset);

            CTask* activeTask = s_getActiveTaskFunc(*(ulong*)((byte*)pedIntelligenceAddr + Ped.CTaskTreePedOffset));
            if (activeTask != null && activeTask->taskTypeIndex == s_cTaskNmScriptControlTypeIndex)
            {
                return true;
            }

            int eventCount = *(int*)((byte*)pedIntelligenceAddr + Ped.CEventCountOffset);
            for (int i = 0; i < eventCount; i++)
            {
                ulong eventAddress = *(ulong*)((byte*)pedIntelligenceAddr + Ped.CEventStackOffset + 8 * ((i + *(int*)((byte*)pedIntelligenceAddr + (Ped.CEventCountOffset - 4)) + 1) % 16));
                if (eventAddress == 0)
                {
                    continue;
                }

                var getEventTypeIndexVirtualFunc = (delegate* unmanaged[Stdcall]<ulong, int>)(*(ulong*)(*(ulong*)eventAddress + s_getEventTypeIndexVFuncOffset));
                if (getEventTypeIndexVirtualFunc(eventAddress) != s_cEventSwitch2NmTypeIndex)
                {
                    continue;
                }

                CTask* taskInEvent = *(CTask**)(eventAddress + 0x28);
                if (taskInEvent == null)
                {
                    continue;
                }

                if (taskInEvent->taskTypeIndex == s_cTaskNmScriptControlTypeIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPedInjured(byte* pedAddress) => *(float*)(pedAddress + 0x280) < *(float*)(pedAddress + Ped.InjuryHealthThresholdOffset);

        private static void SetNmParameters(ulong messageMemory, Dictionary<string, (int value, Type type)> boolIntFloatParameters, Dictionary<string, object> stringVector3ArrayParameters)
        {
            if (boolIntFloatParameters != null)
            {
                foreach (KeyValuePair<string, (int value, Type type)> arg in boolIntFloatParameters)
                {
                    IntPtr name = ScriptDomain.CurrentDomain.PinString(arg.Key);

                    (int argValue, Type argType) = arg.Value;

                    if (argType == typeof(float))
                    {
                        float argValueConverted = *(float*)(&argValue);
                        NativeMemory.s_setNmParameterFloat(messageMemory, name, argValueConverted);
                    }
                    else if (argType == typeof(bool))
                    {
                        bool argValueConverted = argValue != 0 ? true : false;
                        NativeMemory.s_setNmParameterBool(messageMemory, name, argValueConverted);
                    }
                    else if (argType == typeof(int))
                    {
                        NativeMemory.s_setNmParameterInt(messageMemory, name, argValue);
                    }
                }
            }

            if ((stringVector3ArrayParameters != null))
            {
                foreach (KeyValuePair<string, object> arg in stringVector3ArrayParameters)
                {
                    IntPtr name = ScriptDomain.CurrentDomain.PinString(arg.Key);

                    object argValue = arg.Value;
                    switch (argValue)
                    {
                        case float[] vector3ArgValue:
                            NativeMemory.s_setNmParameterVector(messageMemory, name, vector3ArgValue[0], vector3ArgValue[1], vector3ArgValue[2]);
                            break;
                        case string stringArgValue:
                            NativeMemory.s_setNmParameterString(messageMemory, name, ScriptDomain.CurrentDomain.PinString(stringArgValue));
                            break;
                    }
                }
            }
        }

        internal sealed class NmMessageTask : IScriptTask
        {
            #region Fields

            private int _targetHandle;
            private string _messageName;
            private Dictionary<string, (int value, Type type)> _boolIntFloatParameters;
            private Dictionary<string, object> _stringVector3ArrayParameters;
            #endregion

            internal NmMessageTask(int target, string messageName, Dictionary<string, (int value, Type type)> boolIntFloatParameters, Dictionary<string, object> stringVector3ArrayParameters)
            {
                _targetHandle = target;
                this._messageName = messageName;
                this._boolIntFloatParameters = boolIntFloatParameters;
                this._stringVector3ArrayParameters = stringVector3ArrayParameters;
            }

            public void Run()
            {
                byte* pedAddress = (byte*)NativeMemory.GetEntityAddress(_targetHandle).ToPointer();

                if (pedAddress == null)
                {
                    return;
                }

                if (!IsTaskNmScriptControlOrEventSwitch2NmActive(new IntPtr(pedAddress)))
                {
                    return;
                }

                ulong messageMemory = (ulong)AllocCoTaskMem(0x1218).ToInt64();
                if (messageMemory == 0)
                {
                    return;
                }

                s_initMessageMemoryFunc(messageMemory, messageMemory + 0x18, 0x40);

                SetNmParameters(messageMemory, _boolIntFloatParameters, _stringVector3ArrayParameters);

                ulong fragInstNmGtaAddress = *(ulong*)(pedAddress + s_fragInstNmGtaOffset);
                IntPtr messageStringPtr = ScriptDomain.CurrentDomain.PinString(_messageName);
                s_sendNmMessageToPedFunc((ulong)fragInstNmGtaAddress, messageStringPtr, messageMemory);

                FreeCoTaskMem(new IntPtr((long)messageMemory));
            }
        }

        public static void SendNmMessage(int targetHandle, string messageName, Dictionary<string, (int value, Type type)> boolIntFloatParameters, Dictionary<string, object> stringVector3ArrayParameters)
        {
            var task = new NmMessageTask(targetHandle, messageName, boolIntFloatParameters, stringVector3ArrayParameters);
            ScriptDomain.CurrentDomain.ExecuteTaskWithGameThreadTlsContext(task);
        }

        #endregion

        #region -- Helper Functions for Enhanced --


        private static ulong s_entityPosVFuncSecondArgument;
        private static ulong s_entityVPtr;
        private static delegate* unmanaged[Cdecl]<ulong, ulong, byte> s_isEntityUsableVFunc;
        private static int s_entityInternalTypeOffset;
        private static int s_pedEntityPosSecondCheckOffset;
        private static int s_pedEntityInVehicleCheckOffset; // Offset within a Ped instance, where the address of the current Vehicle is stored.
        private static int s_entityPosFloatsOffset;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool IsEntityUsable(ulong entity)
        {
            if (entity == 0)
            {
                return false;
            }

            try
            {
                if (s_isEntityUsableVFunc == null || s_entityVPtr == 0) // TODO: Check if all entity types have the same s_entityVPtr
                {
                    s_entityVPtr = *(ulong*)entity;
                    if (s_entityVPtr == 0)
                    {
                        return false;
                    }
                    IntPtr funcPtr = new IntPtr(*(long*)(s_entityVPtr + 0x28));
                    if (funcPtr.Equals(IntPtr.Zero))
                    {
                        return false;
                    }
                    s_isEntityUsableVFunc = (delegate* unmanaged[Cdecl]<ulong, ulong, byte>)(funcPtr);
                }
                int result = s_isEntityUsableVFunc(entity, s_entityPosVFuncSecondArgument);
                return result != 0;
            }
            catch (Exception)
            {
                // TODO: Add a reasonable Exception Class to throw in this case.
                return false;
            }
        }

        static void GetEntityPos(ulong address, float* position)
        {
            if (address == 0)
            {
                position[0] = 0;
                position[1] = 0;
                position[2] = 0;
            }
            else
            {
                if (s_isEnhanced)
                {
                    // No unique pattern could be found for entityPosFunc in Enhanced, so we have to implement it ourselves.

                    if (IsEntityUsable(address))
                    {
                        if (GetEntityTypeInternal(address) == EntityTypeInternal.Ped)
                        {
                            if (*(byte*)((long)address + s_pedEntityPosSecondCheckOffset) != 0x40)
                            {
                                var vehicleAddress = GetVehiclePedIsIn(address);
                                if (vehicleAddress != 0)
                                {
                                    address = vehicleAddress;
                                }
                            }
                        }
                        position[0] = *(float*)((long)address + s_entityPosFloatsOffset); 
                        position[1] = *(float*)((long)address + s_entityPosFloatsOffset + 4);
                        position[2] = *(float*)((long)address + s_entityPosFloatsOffset + 8);
                    } else
                    {
                        position[0] = 0;
                        position[1] = 0;
                        position[2] = 0;
                    }
                }
                else
                {
                    NativeMemory.s_entityPosFunc(address, position);
                }
            }
        }

        static EntityTypeInternal GetEntityTypeInternal(ulong address)
        {
            return (EntityTypeInternal)(*(byte*)((long)address + s_entityInternalTypeOffset));
        }

        static ulong GetVehiclePedIsIn(ulong address)
        {
            return *(ulong*)((long)address + s_pedEntityInVehicleCheckOffset);
        }

        static void jmpPatchHelper(byte* address, byte jmpLength)
        {
            int jmpInstructionLength = 2;
            byte[] patchBytes = { 0xEB, jmpLength, 0x90 };
            Marshal.Copy(patchBytes, 0, new IntPtr(address), patchBytes.Length);
            int bytesToWriteInstructions = jmpLength - patchBytes.Length + jmpInstructionLength;
            byte[] nopBytes = Enumerable.Repeat((byte)0x90, bytesToWriteInstructions).ToArray();
            Marshal.Copy(nopBytes, 0, new IntPtr(address + patchBytes.Length), nopBytes.Length);
        }

        static ulong Rol(ulong value, int count)
        {
            count &= 63;
            return (value << count) | (value >> (64 - count));
        }
        #endregion
    }
}
