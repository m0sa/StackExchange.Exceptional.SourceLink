using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace StackExchange.Exceptional.SourceLink
{
    static partial class Native
    {

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