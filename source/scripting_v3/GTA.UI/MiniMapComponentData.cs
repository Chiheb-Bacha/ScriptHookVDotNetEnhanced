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

        /// <summary>
        /// Initializes a new instance of the <see cref="MiniMapComponentData"/> class for the specified minimap component.
        /// </summary>
        /// <param name="component">
        /// The <see cref="MiniMapComponent"/> whose layout data should be accessed or modified.
        /// </param>
        public MiniMapComponentData(MiniMapComponent component)
        {
            int componentNameIndex = (int)component;
            if (componentNameIndex < 0 || componentNameIndex >= MiniMapComponentNames.Length)
                return;

            _name = MiniMapComponentNames[componentNameIndex];
            _memoryAddress = SHVDN.NativeMemory.GetMinimapComponentDataPtr(_name);
        }

        /// <summary>
        /// Gets the string identifier used internally by the game for this minimap component.
        /// </summary>
        /// <returns>
        /// The component name used to resolve the minimap component data entry.
        /// </returns>
        public string GetName() { return _name; }

        /// <summary>
        /// Updates the alignment, position, and size of this minimap component.
        /// </summary>
        /// <param name="alignX">
        /// The horizontal alignment of the component on the screen.
        /// </param>
        /// <param name="alignY">
        /// The vertical alignment of the component on the screen.
        /// </param>
        /// <param name="position">
        /// The screen position of the component.
        /// </param>
        /// <param name="size">
        /// The screen size of the component.
        /// </param>
        public void SetData(AlignX alignX, AlignY alignY, Vector2 position, Vector2 size)
        {
            SHVDN.NativeMemory.SetMinimapComponentData(_memoryAddress, (byte)alignX, (byte)alignY, position.X, position.Y, size.X, size.Y);
        }

        /// <summary>
        /// Updates the alignment, position, and size of the specified minimap component.
        /// </summary>
        /// <param name="component">
        /// The minimap component to modify.
        /// </param>
        /// <param name="alignX">
        /// The horizontal alignment of the component.
        /// </param>
        /// <param name="alignY">
        /// The vertical alignment of the component.
        /// </param>
        /// <param name="position">
        /// The screen position of the component.
        /// </param>
        /// <param name="size">
        /// The screen size of the component.
        /// </param>
        public static void SetMiniMapComponentData(MiniMapComponent component, AlignX alignX, AlignY alignY, Vector2 position, Vector2 size)
        {
            int componentNameIndex = (int)component;
            if (componentNameIndex < 0 || componentNameIndex >= MiniMapComponentNames.Length)
                return;

            string name = MiniMapComponentNames[componentNameIndex];
            SHVDN.NativeMemory.SetMinimapComponentData(name, (byte)alignX, (byte)alignY, position.X, position.Y, size.X, size.Y);
        }

        /// <summary>
        /// Retrieves the alignment, position, and size of this minimap component.
        /// </summary>
        /// <param name="alignX">
        /// When this method returns, contains the horizontal alignment of the component.
        /// </param>
        /// <param name="alignY">
        /// When this method returns, contains the vertical alignment of the component.
        /// </param>
        /// <param name="position">
        /// When this method returns, contains the screen position of the component.
        /// </param>
        /// <param name="size">
        /// When this method returns, contains the screen size of the component.
        /// </param>
        public void GetData(out AlignX alignX, out AlignY alignY, out Vector2 position, out Vector2 size)
        {
            byte alignXByte, alignYByte;
            SHVDN.NativeMemory.GetMinimapComponentData(_memoryAddress, out alignXByte, out alignYByte, out position.X, out position.Y, out size.X, out size.Y);
            alignX = (AlignX)alignXByte;
            alignY = (AlignY)alignYByte;
        }

        /// <summary>
        /// Retrieves the alignment, position, and size of the specified minimap component.
        /// </summary>
        /// <param name="component">
        /// The minimap component to query.
        /// </param>
        /// <param name="alignX">
        /// When this method returns, contains the horizontal alignment of the component.
        /// </param>
        /// <param name="alignY">
        /// When this method returns, contains the vertical alignment of the component.
        /// </param>
        /// <param name="position">
        /// When this method returns, contains the screen position of the component.
        /// </param>
        /// <param name="size">
        /// When this method returns, contains the screen size of the component.
        /// </param>
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
