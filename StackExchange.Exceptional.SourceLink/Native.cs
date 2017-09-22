using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace StackExchange.Exceptional.SourceLink
{
    static partial class Native
    {

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetDllDirectory(string path);

        public static bool WINAPI(bool BOOL) => !BOOL ? throw Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()) : BOOL;
    }
}