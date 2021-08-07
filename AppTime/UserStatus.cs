using System;
using System.Runtime.InteropServices;

namespace AppTime
{
    class UserStatus
    {
        /// <summary>
        /// The LASTINPUTINFO structure contains the time of the last input.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO
        {
            public static readonly int SizeOf = Marshal.SizeOf(typeof(LASTINPUTINFO));
            [MarshalAs(UnmanagedType.U4)]
            public uint cbSize;
            [MarshalAs(UnmanagedType.U4)]
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        /// <summary>
        /// Gets the time of the last user input (in ms since the system started)
        /// </summary>
        public static uint GetLastInputTime()
        {
            uint count = 0;
            LASTINPUTINFO vLastInputInfo = new();
            vLastInputInfo.cbSize = (uint)Marshal.SizeOf(vLastInputInfo);
            vLastInputInfo.dwTime = 0;
            if (GetLastInputInfo(ref vLastInputInfo))
            {
                count = (uint)Environment.TickCount - vLastInputInfo.dwTime;
            }
            return (count > 0) ? (count / 1000) : 0;
        }
    }
}
