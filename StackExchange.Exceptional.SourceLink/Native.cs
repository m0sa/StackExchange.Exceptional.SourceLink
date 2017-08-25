using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace StackExchange.Exceptional.SourceLink
{
    static partial class Native
    {
        static Native()
        {
            WINAPI(SetDllDirectory(Environment.Is64BitProcess ? "x64" : "x86"));
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string path);

        public static bool WINAPI(bool BOOL)
        {
            if (!BOOL)
            {
                throw Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error());
            }
            return BOOL;
        }
    }
}