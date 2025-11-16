//
// Copyright (C) 2025 Chiheb-Bacha
// License: https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced#license
//
// MinHook License can be found in MinHookLicense.txt
//
// Parts of CallHook (FindPrevFreeRegion & AllocateFunctionStubForCallHook) were taken from NTAuthority (BasTimmer), altered, and rewritten in C#.
// License can be found in CallHookLicense.txt
//

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Linq;

namespace SHVDN
{
    public unsafe static class Hooking
    {
        #region -- Hook Management --

        public enum HookType
        {
            MinHook = 0,
            CallHook = 1,
        }

        public enum HookOperationStatus : byte
        {
            Success = 0,
            HookAlreadyExistsError = 1,
            NotHookOwnerError = 2,
            HookDoesNotExistError = 3,
            HookTypeInvalidError = 4,
            HookSiteAddressInvalid = 5,
            HookFunctionAddressInvalid = 6,
            MH_InitializationFailError = 7,
            MH_HookCreationFailError = 8,
            AllocateFunctionStubFailError = 9,
            CH_EnableHookFailError = 10,
            CH_DisableHookFailError = 11,
            CreateHookFailError = 12,
            CH_RemoveHookFailError = 13,
        }

        // We could make this into a base class and have derived classes (e.g. MinHookHandle, CallHookHandle, ...),
        // however, we currently only have 3 attributes that are different between the two types (totalling 16 wasted bytes (probably less) for each instance of HookHandle of type MinHook)
        // and we normally don't create that many hooks, which makes the size of unused attributes insignificant.
        // So keeping a universal HookHandle class makes more sense.
        public class HookHandle
        {
            public IntPtr HookSite; // address of the HookSite. Address of the HookedFunction if Type==HookType.MinHook, Address of the E8 Call if Type==HookType.CallHook.
            public IntPtr HookFunction; // pointer to our hook (or delegate pointer)
            public IntPtr Stub; // allocated stub, IntPtr.Zero if no stub is needed. Not used if Type==HookType.MinHook.
            public IntPtr OriginalTarget; // absolute address the call originally pointed to (call + 5 + origRel) if Type==CallHook, absolute address of the relocated hooked function uf Type==MinHook.
            public int OriginalRel; // original rel32 operand. Not used if Type==HookType.MinHook.
            public int NewRel; // new rel32 operand. Not used if Type==HookType.MinHook.
            public bool Active; // Whether the hook is enabled or disabled.
            public HookType Type;
            public string HookName;
            public string HookOwner;
            public bool IsSHVDNEHook;
            public HookOperationStatus Status;

            public HookHandle() { }
        }

        private static readonly ConcurrentDictionary<IntPtr, HookHandle> hookPool = new ConcurrentDictionary<IntPtr, HookHandle>();

        public static unsafe HookHandle CreateHook(IntPtr hookSite, IntPtr hookFunction, HookType hookType, string hookName = "")
        {
            HookHandle hookHandle = new HookHandle();

            if (hookSite == IntPtr.Zero) {
                Log.Message(Log.Level.Warning, $"Hook Site Address is invalid (IntPtr.Zero).");
                hookHandle.Status = HookOperationStatus.HookSiteAddressInvalid;
                return hookHandle;
            }
            if (hookFunction == IntPtr.Zero)
            {
                Log.Message(Log.Level.Warning, $"Hook Function Address is invalid (IntPtr.Zero).");
                hookHandle.Status = HookOperationStatus.HookFunctionAddressInvalid;
                return hookHandle;
            }

            // SHVDNE installs hooks before creating scripts. At that time, ScriptDomain.ExecutingScript is null.
            // We use that to know if a hook is installed by SHVDNE or by a script.
            var executingScript = ScriptDomain.ExecutingScript;
            string hookOwnerName = (executingScript == null) ? "SHVNDE" : executingScript.Name;
            bool isSHVDNEHook = (executingScript == null);

            HookHandle existingHandle;
            var isHandlePresent = hookPool.TryGetValue(hookSite, out existingHandle);

            if (isHandlePresent)
            {
                string hookOwnerString = existingHandle.IsSHVDNEHook ? existingHandle.HookOwner : $"the script \"{existingHandle.HookOwner}\""; // This is done so scripts cannot impersonate SHVDNE
                Log.Message(Log.Level.Warning, $"{hookOwnerName} could not create the hook with the name \"{hookName}\"," +
                    $"because another hook of type {existingHandle.Type} with the name \"{existingHandle.HookName}\" was created by {hookOwnerString} at the same address {hookSite.ToInt64().ToString("X")}");

                return new HookHandle
                {
                    Status = HookOperationStatus.HookAlreadyExistsError,
                    Type = existingHandle.Type,
                    HookName = existingHandle.HookName,
                    HookOwner = hookOwnerString,
                };
            }

            if (hookType == HookType.MinHook)
            {
                if (!MH_INITIALIZED)
                {
                    int MH_INIT_STATUS = MH_Initialize();
                    if (MH_INIT_STATUS != ((int)MH_STATUS.MH_OK)  && MH_INIT_STATUS != ((int)MH_STATUS.MH_ERROR_ALREADY_INITIALIZED))
                    {
                        Log.Message(Log.Level.Warning, "MinHook initialization failed.");
                        hookHandle.Status = HookOperationStatus.MH_InitializationFailError;
                        return hookHandle;
                    }
                    MH_INITIALIZED = true;
                }
                IntPtr originalTarget = new IntPtr();
                if (MH_CreateHook(hookSite, hookFunction, out originalTarget) != 0) {
                    Log.Message(Log.Level.Warning, $"MinHook failed to create the hook \"{hookName}\" by \"{hookOwnerName}\"");
                    hookHandle.Status = HookOperationStatus.MH_HookCreationFailError;
                    return hookHandle;
                }
                hookHandle.HookSite = hookSite;
                hookHandle.HookFunction = hookFunction;
                hookHandle.OriginalTarget = originalTarget;
                hookHandle.Type = HookType.MinHook;
            }
            else if (hookType == HookType.CallHook)
            {
                 hookHandle = CH_CreateHook(hookSite, hookFunction, hookName);
            }
            else
            {
                hookHandle.Status = HookOperationStatus.HookTypeInvalidError;
                return hookHandle;
            }

            hookHandle.Status = HookOperationStatus.Success;
            bool hookAdded;
            try
            {
                hookAdded = hookPool.TryAdd(hookHandle.HookSite, hookHandle);
            }
            catch (Exception ex)
            {
                Log.Message(Log.Level.Warning, $"CreateHook failed to add the hook {hookHandle.HookName}, of type {hookHandle.Type}, by {hookHandle.HookOwner}. An exception was thrown: {ex.ToString()}");
                hookHandle = new HookHandle();
                hookHandle.Status = HookOperationStatus.CreateHookFailError;
                return hookHandle;
            }

            hookHandle.IsSHVDNEHook = isSHVDNEHook;
            hookHandle.HookOwner = hookOwnerName;
            hookHandle.HookName = hookName;
            Log.Message(Log.Level.Info, $"CreateHook: {hookHandle.HookOwner} created the hook {hookHandle.HookName}, of type {hookHandle.Type}, at 0x{hookHandle.HookSite.ToInt64().ToString("X")}, " +
                $"with OriginalTarget at 0x{hookHandle.OriginalTarget.ToInt64().ToString("X")}" +
                // Handle HookType-specific Log messages.
                ((hookHandle.Type == HookType.CallHook) ? $", Stub at 0x{hookHandle.Stub.ToInt64().ToString("X")}."
                : "."));
            return hookHandle;
        }

        public static void RemoveHook(HookHandle hookHandle)
        {
            if (hookHandle == null)
            {
                return;
            }

            var executingScript = ScriptDomain.ExecutingScript;
            bool isSHVDNE = (executingScript == null);
            string scriptName = isSHVDNE ? "SHVDNE" : executingScript.Name;

            if (!isSHVDNE && (hookHandle.HookOwner != scriptName))
            {
                Log.Message(Log.Level.Warning, $"Script {scriptName} cannot modify the hook {hookHandle.HookName} of type {hookHandle.Type}, as it was created by {hookHandle.HookOwner}");
                return;
            }

            DisableHook(hookHandle);
            if (hookHandle.Type == HookType.MinHook)
            {
                MH_RemoveHook(hookHandle.HookSite);
                hookPool.TryRemove(hookHandle.HookSite, out _);
                hookHandle = default;
            }
            else if (hookHandle.Type == HookType.CallHook)
            {
                CH_RemoveHook(hookHandle);
            }
        }

        public static void RemoveAllHooks()
        {
            DisableAllHooks();

            foreach (var hookHandle in hookPool.Values.ToList())
            {
                RemoveHook(hookHandle);
            }
            hookPool.Clear();
            FreeAllStubPages();
            MH_Uninitialize();
            MH_INITIALIZED = false;
        }

        #region -- Related to hooks installed by SHVDNE Scripts --
        // As of 1.1.0.0, only SHVDNE can add hooks. However, it is planned for scripts to be able to add hooks through the API in the future.

        // This removes all the hooks installed by SHVDNE scripts, and not SHVDNE itself.
        // Called by SHVDNE to remove all hooks installed by scripts when unloading the script domain.
        public static void RemoveAllScriptHooks()
        {
            foreach (var hookHandle in hookPool.Values.ToList())
            {
                if (!hookHandle.IsSHVDNEHook) Hooking.RemoveHook(hookHandle);
            }
        }

        // Removes/Enables/Disables the hooks of the script whose name equals scriptName.
        // Called by SHVDNE to remove/enable/disable hooks installed by specific scripts if they are aborted/paused/resumed

        public static void RemoveAllScriptHooksByScriptName(string scriptName)
        {
            foreach (var hookHandle in hookPool.Values.ToList())
            {
                if (!hookHandle.IsSHVDNEHook && scriptName != null && scriptName.Equals(hookHandle.HookOwner))
                {
                    Hooking.RemoveHook(hookHandle);
                }
            }
        }

        public static void EnableAllScriptHooksByScriptName(string scriptName)
        {
            foreach (var hookHandle in hookPool.Values.ToList())
            {
                if (!hookHandle.IsSHVDNEHook && scriptName != null && scriptName.Equals(hookHandle.HookOwner))
                {
                    Hooking.EnableHook(hookHandle);
                }
            }
        }

        public static void DisableAllScriptHooksByScriptName(string scriptName)
        {
            foreach (var hookHandle in hookPool.Values.ToList())
            {
                if (!hookHandle.IsSHVDNEHook && scriptName != null && scriptName.Equals(hookHandle.HookOwner))
                {
                    Hooking.DisableHook(hookHandle);
                }
            }
        }

        #region -- Unused Hooking operations on Script-installed hooks --
        // Unused as of 1.1.0.0 as scripts can't install their own hooks.
        // Would be useful when hooking is exposed as part of the API.

        // Removes/Enables/Disables the hooks of the calling script, based on ScriptDomain.ExecutingScript.Name
        // Can be called by scripts to remove/disable/enable their own hooks

        private static void RemoveScriptHooksOfCallingScript()
        {
            var executingScript = ScriptDomain.ExecutingScript;
            if (executingScript != null)
            {
                foreach (var hookHandle in hookPool.Values.ToList())
                {
                    if (executingScript.Name.Equals(hookHandle.HookOwner))
                    {
                        Hooking.RemoveHook(hookHandle);
                    }
                }
            }
        }

        private static void EnableAllScriptHooksOfCallingScript()
        {
            var executingScript = ScriptDomain.ExecutingScript;
            if (executingScript != null)
            {
                foreach (var hookHandle in hookPool.Values.ToList())
                {
                    if (executingScript.Name.Equals(hookHandle.HookOwner))
                    {
                        Hooking.EnableHook(hookHandle);
                    }
                }
            }
        }

        private static void DisableAllScriptHooksOfCallingScript()
        {
            var executingScript = ScriptDomain.ExecutingScript;
            if (executingScript != null)
            {
                foreach (var hookHandle in hookPool.Values.ToList())
                {
                    if (executingScript.Name.Equals(hookHandle.HookOwner))
                    {
                        Hooking.DisableHook(hookHandle);
                    }
                }
            }
        }

        #endregion

        #endregion

        public static void EnableHook(HookHandle hookHandle)
        {
            ModifyHookState(hookHandle, true);
        }

        public static void DisableHook(HookHandle hookHandle)
        {
            ModifyHookState(hookHandle, false);
        }

        private static void ModifyHookState(HookHandle hookHandle, bool newState) 
        {
            if (hookHandle == null) return;

            var executingScript = ScriptDomain.ExecutingScript;
            bool isSHVDNE = (executingScript == null);
            string scriptName = isSHVDNE ? "SHVDNE" : executingScript.Name;

            if (!isSHVDNE && !(hookHandle.HookOwner.Equals(scriptName)))
            {
                Log.Message(Log.Level.Warning, $"Script {scriptName} cannot modify the hook {hookHandle.HookName} of type {hookHandle.Type}, as it was created by {hookHandle.HookOwner}");
                return;
            }

            if (hookHandle.Type == HookType.MinHook)
            {
                if (newState)
                {
                    MH_EnableHook(hookHandle.HookSite);
                }
                else
                {
                    MH_DisableHook(hookHandle.HookSite);
                }
            }
            else if (hookHandle.Type == HookType.CallHook)
            {
                if (newState)
                {
                    CH_EnableHook(hookHandle);
                }
                else
                {
                    CH_DisableHook(hookHandle);
                }
            }
        }

        public static void EnableAllHooks()
        {
            ModifyAllHooks(true);
        }

        public static void DisableAllHooks()
        {
            ModifyAllHooks(false);
        }

        private static void ModifyAllHooks(bool newState)
        {
            Log.Message(Log.Level.Warning, $"ModifyAllHooksCalled, newState {newState}");
            foreach (var hook in hookPool.Values)
            {
                Log.Message(Log.Level.Warning, $"ModifyAllHooksCalled, hook {hook.HookName}, state {hook.Active}");
                if (hook.Type == HookType.MinHook)
                {
                    Log.Message(Log.Level.Warning, $"ModifyAllHooksCalled, type {HookType.MinHook}");
                    if (newState)
                    {
                        MH_EnableHook(hook.HookSite);
                    }
                    else
                    {
                        MH_DisableHook(hook.HookSite);
                    }
                    hook.Active = newState;
                }
                else if (hook.Type == HookType.CallHook)
                {
                    Log.Message(Log.Level.Warning, $"ModifyAllHooksCalled, type {HookType.CallHook}");
                    if (newState)
                    {
                        CH_EnableHook(hook);
                    }
                    else
                    {
                        CH_DisableHook(hook);
                    }
                }
                Log.Message(Log.Level.Warning, $"iter done");
            }
            Log.Message(Log.Level.Warning, $"ModifyAllHooks done");
        }

        #endregion

        #region -- MinHook --

        private static bool MH_INITIALIZED = false;

        private const string DllName = "MinHook.x64.dll";

        private static IntPtr MH_ALL_HOOKS = IntPtr.Zero;

        private enum MH_STATUS
        {
            MH_OK = 0,
            MH_ERROR_ALREADY_INITIALIZED = 1,
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int MH_Initialize();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int MH_CreateHook(
            IntPtr pTarget,
            IntPtr pDetour,
            out IntPtr ppOriginal);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int MH_EnableHook(IntPtr pTarget);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int MH_DisableHook(IntPtr pTarget);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int MH_Uninitialize();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int MH_RemoveHook(IntPtr target);

        #endregion

        #region -- CallHook --

        private const int MEMORY_BLOCK_SIZE = 0x1000; // page size
        private const long MAX_MEMORY_RANGE = 0x40000000; // 1GB
        private const int STUB_SIZE = 0x10; // 16 bytes per stub (aligned)
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private static readonly object stubListLock = new();
        private static readonly List<CH_StubPage> stubPages = new List<CH_StubPage>();

        [SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);
        private const uint MEM_RELEASE = 0x8000;

        [SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32.dll")]
        private static extern void GetSystemInfo(out SYSTEM_INFO lpSystemInfo);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int VirtualQuery(IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, UIntPtr dwSize);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_INFO
        {
            public ushort processorArchitecture;
            public ushort reserved;
            public uint pageSize;
            public IntPtr lpMinimumApplicationAddress;
            public IntPtr lpMaximumApplicationAddress;
            public IntPtr dwActiveProcessorMask;
            public uint numberOfProcessors;
            public uint processorType;
            public uint allocationGranularity;
            public ushort processorLevel;
            public ushort processorRevision;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        private unsafe class CH_StubPage
        {
            public int Capacity;
            private int usedBytes; // should only be updated atomically, and so should stay private
            public byte* pageBase;

            public CH_StubPage(byte* pageBase)
            {
                this.pageBase = pageBase;
                this.usedBytes = 0;
                this.Capacity = MEMORY_BLOCK_SIZE;
            }

            // Try to allocate 'size' bytes (aligned to STUB_SIZE)
            // Returns pointer in out param if successful.
            public bool TryAllocate(int size, out byte* ptr)
            {
                int aligned = (usedBytes + (STUB_SIZE - 1)) & ~(STUB_SIZE - 1);
                if (aligned + size > Capacity)
                {
                    ptr = null;
                    return false;
                }

                ptr = pageBase + aligned;
                usedBytes = aligned + size;
                return true;
            }

            public bool IsWithinJmpRange(byte* callAddress)
            {
                const long MaxRel = (long)int.MaxValue;
                const int RelInstrLen = 5; // rel32 target is computed from next instruction

                // compute the actual aligned start where the next stub will be written
                long alignedStart = ((long)usedBytes + (STUB_SIZE - 1)) & ~((long)STUB_SIZE - 1);
                long startAddr = (long)pageBase + alignedStart;

                long rel = startAddr - ((long)callAddress + RelInstrLen);
                return Math.Abs(rel) <= MaxRel;
            }
        }

        private static IntPtr FindPrevFreeRegion(IntPtr address, IntPtr minAddr, uint allocationGranularity)
        {
            long tryAddr = address.ToInt64();
            tryAddr -= tryAddr % allocationGranularity;
            tryAddr -= allocationGranularity;

            while (tryAddr >= minAddr.ToInt64())
            {
                MEMORY_BASIC_INFORMATION mbi;
                if (VirtualQuery((IntPtr)tryAddr, out mbi, (uint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == 0)
                    break;

                const uint MEM_FREE = 0x10000;
                if (mbi.State == MEM_FREE)
                    return (IntPtr)tryAddr;

                if (mbi.AllocationBase.ToInt64() < allocationGranularity)
                    break;

                tryAddr = mbi.AllocationBase.ToInt64() - allocationGranularity;
            }

            return IntPtr.Zero;
        }

        private static CH_StubPage AllocateNewStubPageNear(IntPtr origin)
        {
            SYSTEM_INFO si;
            GetSystemInfo(out si);

            long minAddr = si.lpMinimumApplicationAddress.ToInt64();
            if (origin.ToInt64() > MAX_MEMORY_RANGE && minAddr < origin.ToInt64() - MAX_MEMORY_RANGE)
                minAddr = origin.ToInt64() - MAX_MEMORY_RANGE;

            IntPtr pAlloc = origin;
            while (pAlloc.ToInt64() >= minAddr)
            {
                pAlloc = FindPrevFreeRegion(pAlloc, (IntPtr)minAddr, si.allocationGranularity);
                if (pAlloc == IntPtr.Zero) break;

                IntPtr stub = VirtualAlloc(pAlloc, (UIntPtr)MEMORY_BLOCK_SIZE, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
                if (stub != IntPtr.Zero)
                {
                    return new CH_StubPage((byte*)stub);
                }
            }

            return null;
        }

        // Thread safety is necessary, since multiple scripts could add hooks at the same time.
        // As of 1.1.0.0, only SHVDNE can add hooks. However, it is planned for scripts to be able to add hooks through the API in the future.
        private static unsafe byte* AllocateFunctionStubForCallHook(IntPtr origin, IntPtr function, int type = 0)
        {
            lock (stubListLock)
            {
                foreach (var page in stubPages)
                {
                    if (!page.IsWithinJmpRange((byte*)origin.ToInt64())) continue;
                    if (page.TryAllocate(STUB_SIZE, out byte* ptr))
                    {
                        WriteAbsoluteJumpStub(ptr, function, type);
                        return ptr;
                    }
                }

                var newPage = AllocateNewStubPageNear(origin);
                if (newPage == null) return null;
                stubPages.Add(newPage);

                if (!newPage.TryAllocate(STUB_SIZE, out byte* newPtr)) return null;
                WriteAbsoluteJumpStub(newPtr, function, type);
                return newPtr;
            }
        }

        // mov rax, imm64; jmp rax; pad to STUB_SIZE (0x10) with 0xCC
        // type is currently unused, but keeping it if I ever need to use another register instead of rax (1 for rcx, ...)
        private static unsafe void WriteAbsoluteJumpStub(byte* code, IntPtr function, int type = 0)
        {
            code[0] = 0x48;
            code[1] = (byte)(0xB8 | (type & 0x7)); // mov r(ax -- depends on type),
            *(ulong*)(code + 2) = (ulong)function.ToInt64(); // imm64
            *(ushort*)(code + 10) = (ushort)(0xE0FF | ((type & 0xFF) << 8)); // jmp r(ax) (FF E0)
            for (int i = 12; i < STUB_SIZE; i++) code[i] = 0xCC;

            FlushInstructionCache(GetCurrentProcess(), (IntPtr)code, (UIntPtr)STUB_SIZE);
        }

        // Used when Removing all Hooks, so that we don't leak memory pages every time we unload a domain.
        private static void FreeAllStubPages()
        {
            lock (stubListLock)
            {
                foreach (var p in stubPages)
                {
                    if (p == null || p.pageBase == null) continue;
                    VirtualFree((IntPtr)p.pageBase, UIntPtr.Zero, MEM_RELEASE);
                }
                stubPages.Clear();
            }
        }

        #endregion

        #region -- CallHook API --

        private static unsafe HookHandle CH_CreateHook(IntPtr callAddress, IntPtr hookFunction, string hookName = "")
        {
            // Read original rel32 operand (4 bytes at callAddress +1)
            IntPtr operandPtr = IntPtr.Add(callAddress, 1);
            int origRel = MemDataMarshal.ReadInt32(operandPtr);

            // Compute original target absolute address
            long originalTargetLong = callAddress.ToInt64() + 5 + origRel;
            IntPtr originalTarget = new IntPtr(originalTargetLong);

            // Compute new rel32 to hook (or to stub if out of range)
            long rel64 = hookFunction.ToInt64() - (callAddress.ToInt64() + 5);
            IntPtr stubPtr = IntPtr.Zero;
            int newRel;
            HookHandle hookHandle = new HookHandle();
            var hookOwner = ScriptDomain.ExecutingScript;
            string hookOwnerName = (hookOwner == null) ? "SHVDNE" : hookOwner.Name;

            if (rel64 < int.MinValue || rel64 > int.MaxValue)
            {
                // Allocate trampoline near callAddress
                byte* stub = AllocateFunctionStubForCallHook(callAddress, hookFunction, 0);
                if (stub == null)
                {
                    Log.Message(Log.Level.Warning, $"Failed to allocate function stub for the hook \"{hookName}\", of type {HookType.CallHook}, created by \"{hookOwnerName}\"");
                    hookHandle.Status = HookOperationStatus.AllocateFunctionStubFailError;
                    return hookHandle;
                }

                stubPtr = (IntPtr)stub;
                long stubRel = stubPtr.ToInt64() - (callAddress.ToInt64() + 5);
                if (stubRel < int.MinValue || stubRel > int.MaxValue)
                {
                    Log.Message(Log.Level.Warning, $"Failed to allocate function stub for the hook \"{hookName}\", of type {HookType.CallHook}, created by \"{hookOwnerName}\"." +
                        $"The trampoline is unexpectedly out of rel32 range.");
                    hookHandle.Status = HookOperationStatus.AllocateFunctionStubFailError;
                    return hookHandle;
                }
                newRel = (int)stubRel;
            }
            else
            {
                newRel = (int)rel64;
            }

            hookHandle.HookSite = callAddress;
            hookHandle.HookFunction = hookFunction;
            hookHandle.Stub = stubPtr;
            hookHandle.OriginalTarget = originalTarget;
            hookHandle.OriginalRel = origRel;
            hookHandle.NewRel = newRel;
            hookHandle.Type = HookType.CallHook;

            return hookHandle;
        }

        private static void CH_EnableHook(HookHandle hookHandle)
        {
            CH_SetHookState(hookHandle, true);
        }

        private static void CH_DisableHook(HookHandle hookHandle)
        {
            CH_SetHookState(hookHandle, false);
        }

        private static void CH_SetHookState(HookHandle hookHandle, bool newState)
        {
            if (!hookPool.ContainsKey(hookHandle.HookSite)) return;
            if (hookHandle.Active == newState) return;
            IntPtr callAddress = hookHandle.HookSite;
            IntPtr operandPtr = IntPtr.Add(callAddress, 1);

            if (!VirtualProtect(callAddress, (UIntPtr)5, PAGE_EXECUTE_READWRITE, out uint oldProt))
            {
                string CH_HookOperationString = newState ? "En" : "Dis";
                Log.Message(Log.Level.Warning, $"VirtualProtect failed in CH_{CH_HookOperationString}ableHook for the hook \"{hookHandle.HookName}\", created by \"{hookHandle.HookOwner}\"");
                hookHandle.Status = HookOperationStatus.CH_DisableHookFailError;
                return;
            }

            var rel32 = newState ? hookHandle.NewRel : hookHandle.OriginalRel;
            MemDataMarshal.WriteInt32(operandPtr, rel32);

            FlushInstructionCache(GetCurrentProcess(), callAddress, (UIntPtr)5);
            VirtualProtect(callAddress, (UIntPtr)5, oldProt, out _);

            hookHandle.Active = newState;
        }

        private static void CH_RemoveHook(HookHandle hookHandle)
        {
            hookPool.TryRemove(hookHandle.HookSite, out _);
        }
        #endregion
    }
}
