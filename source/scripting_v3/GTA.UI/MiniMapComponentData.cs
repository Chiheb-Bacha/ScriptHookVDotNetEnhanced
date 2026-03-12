using System;
using GTA.Math;

namespace GTA.UI
{
    public class MiniMapComponentData
    {
        private IntPtr _memoryAddress;
        private string _name;

        private static readonly string[] MiniMapComponentNames = new string[]
        {
            "minimap",
            "minimap_mask",
            "minimap_blur",
            "bigmap",
            "bigmap_mask",
            "bigmap_blur",
            "pausemap",
            "pausemap_mask",
            "gallery",
            "gallery_mask",
            "golf_courses",
        };

        public MiniMapComponentData(MiniMapComponent component)
        {
            int componentNameIndex = (int)component;
            if (componentNameIndex < 0 || componentNameIndex >= MiniMapComponentNames.Length)
                return;

            _name = MiniMapComponentNames[componentNameIndex];
            _memoryAddress = SHVDN.NativeMemory.GetMinimapComponentDataPtr(_name);
        }

        public string GetName() { return _name; }

        public void SetData(AlignX alignX, AlignY alignY, Vector2 position, Vector2 size)
        {
            SHVDN.NativeMemory.SetMinimapComponentData(_memoryAddress, (byte)alignX, (byte)alignY, position.X, position.Y, size.X, size.Y);
        }

        public static void SetMiniMapComponentData(MiniMapComponent component, AlignX alignX, AlignY alignY, Vector2 position, Vector2 size)
        {
            int componentNameIndex = (int)component;
            if (componentNameIndex < 0 || componentNameIndex >= MiniMapComponentNames.Length)
                return;

            string name = MiniMapComponentNames[componentNameIndex];
            SHVDN.NativeMemory.SetMinimapComponentData(name, (byte)alignX, (byte)alignY, position.X, position.Y, size.X, size.Y);
        }

        public void GetData(out AlignX alignX, out AlignY alignY, out Vector2 position, out Vector2 size)
        {
            byte alignXByte, alignYByte;
            SHVDN.NativeMemory.GetMinimapComponentData(_memoryAddress, out alignXByte, out alignYByte, out position.X, out position.Y, out size.X, out size.Y);
            alignX = (AlignX)alignXByte;
            alignY = (AlignY)alignYByte;
        }

        public static void GetMiniMapComponentData(MiniMapComponent component, out AlignX alignX, out AlignY alignY, out Vector2 position, out Vector2 size)
        {
            int componentNameIndex = (int)component;
            if (componentNameIndex < 0 || componentNameIndex >= MiniMapComponentNames.Length)
            {
                alignX = default;
                alignY = default;
                position = default;
                size = default;
                return;
            }

            string name = MiniMapComponentNames[componentNameIndex];

            byte alignXByte, alignYByte;
            SHVDN.NativeMemory.GetMinimapComponentData(name, out alignXByte, out alignYByte, out position.X, out position.Y, out size.X, out size.Y);
            alignX = (AlignX)alignXByte;
            alignY = (AlignY)alignYByte;
        }
    }
}
