//
// Copyright (C) 2015 crosire & kagikn & contributors
// License: https://github.com/scripthookvdotnet/scripthookvdotnet#license
//

namespace GTA.UI
{
    /// <summary>
    /// An enumeration of all the possible text alignment types for script text draw commands.
    /// </summary>
    public enum Alignment
    {
        Center = 0,
        Left = 1,
        Right = 2,
    }

    public enum AlignX : byte
    {
        Center = (byte)'C',
        Left = (byte)'L',
        Right = (byte)'R',
        Invalid = (byte)'I',
    }

    public enum AlignY : byte
    {
        Center = (byte)'C',
        Top = (byte)'T',
        Bottom = (byte)'B',
        Invalid = (byte)'I',
    }
}
