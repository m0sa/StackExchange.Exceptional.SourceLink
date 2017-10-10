using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace StackExchange.Exceptional.SourceLink
{
    static partial class Native
    {
        public static bool WINAPI(bool BOOL) => !BOOL ? throw Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()) : BOOL;


        // http://www.pinvoke.net/default.aspx/kernel32.LoadLibraryEx
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, LoadLibraryFlags dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FreeLibrary(IntPtr hModule);

        public class LibHandle : SafeHandle
        {
            public LibHandle (string filename, LoadLibraryFlags flags) : base(IntPtr.Zero, true)
            {
                base.SetHandle(LoadLibraryEx(filename, IntPtr.Zero, flags));
                IsInvalid =  this.handle == IntPtr.Zero;
            }

            public override bool IsInvalid { get; }

            protected override bool ReleaseHandle() => FreeLibrary(this.handle);

        }

        [System.Flags]
        public enum LoadLibraryFlags : uint
        {
            DONT_RESOLVE_DLL_REFERENCES = 0x00000001,
            LOAD_IGNORE_CODE_AUTHZ_LEVEL = 0x00000010,
            LOAD_LIBRARY_AS_DATAFILE = 0x00000002,
            LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE = 0x00000040,
            LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020,
            LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200,
            LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100,
            LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800,
            LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400,
            LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008
        }

    }
}