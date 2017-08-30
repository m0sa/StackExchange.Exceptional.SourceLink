using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace StackExchange.Exceptional.SourceLink
{
    partial class Native
    {

        /// <summary>
        /// https://msdn.microsoft.com/en-us/library/windows/desktop/ms679291.aspx#SYMBOL_SERVER
        /// </summary>
        public const string DbgHelp = "dbghelp.dll";

        /// <summary>
        /// An application-defined callback function used with the <see cref="SymRegisterCallback"/> function. It is called by the symbol handler.
        /// </summary>
        /// <param name="hProcess">A handle to the process that was originally passed to the SymInitialize function.</param>
        /// <param name="ActionCode"><see cref="SymActionCode"/></param>
        /// <param name="CallbackData">Data for the operation. The format of this data depends on the value of the ActionCode parameter.</param>
        /// <param name="UserContext">User-defined value specified in SymRegisterCallback, or NULL. Typically, this parameter is used by an application to pass a pointer to a data structure that lets the callback function establish some context.</param>
        /// <returns>
        /// To indicate success handling the code, return TRUE.
        /// <br/>
        /// To indicate failure handling the code, return FALSE.
        /// If your code does not handle a particular code, you should also return FALSE.
        /// (Returning TRUE in this case may have unintended consequences.)
        /// </returns>
        /// <remarks>
        /// BOOL CALLBACK PSYMBOL_REGISTERED_CALLBACK (
        ///     _In_     HANDLE  hProcess,
        ///     _In_     ULONG   ActionCode,
        ///     _In_opt_ PVOID   CallbackData,
        ///     _In_opt_ PVOID   UserContext
        /// );
        /// </remarks>
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public delegate bool SymRegisterCallbackProc(IntPtr hProcess, SymActionCode ActionCode, IntPtr CallbackData, IntPtr UserContext);

        public static class DbgHelpImports
        {
            /// <summary>
            /// Initializes the symbol handler for a process.
            /// </summary>
            /// <param name="hProcess">
            /// A handle that identifies the caller. This value should be unique and nonzero, but need not be a process handle. However, if you do use a process handle, be sure to use the correct handle. If the application is a debugger, use the process handle for the process being debugged. Do not use the handle returned by GetCurrentProcess when debugging another process, because calling functions like SymLoadModuleEx can have unexpected results.
            /// This parameter cannot be NULL.
            /// </param>
            /// <param name="UserSearchPath">
            /// The path, or series of paths separated by a semicolon (;), that is used to search for symbol files. If this parameter is NULL, the library attempts to form a symbol path from the following sources:
            /// <ul>
            ///     <li>The current working directory of the application</li>
            ///     <li>The _NT_SYMBOL_PATH environment variable</li>
            ///     <li>The _NT_ALTERNATE_SYMBOL_PATH environment variable</li>
            /// </ul>
            /// Note that the search path can also be set using the SymSetSearchPath function.
            /// </param>
            /// <param name="fInvadeProcess">
            /// If this value is TRUE, enumerates the loaded modules for the process and effectively calls the SymLoadModule function for each module.
            /// </param>
            /// <returns></returns>
            /// <remarks>
            /// BOOL WINAPI SymInitialize(
            ///     _In_     HANDLE hProcess,
            ///     _In_opt_ PCTSTR UserSearchPath,
            ///     _In_     BOOL   fInvadeProcess
            /// );
            /// </remarks>
            [DllImport(DbgHelp, CharSet = CharSet.Auto, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SymInitialize(IntPtr hProcess, string UserSearchPath, bool fInvadeProcess);

            /// <summary>
            /// Deallocates all resources associated with the process handle.
            /// </summary>
            [DllImport(DbgHelp, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SymCleanup(IntPtr hProcess);

            /// <summary>
            /// Sets the options mask.
            /// </summary>
            /// <remarks>DWORD WINAPI SymSetOptions(_In_ DWORD SymOptions);</remarks>
            [DllImport(DbgHelp)]
            public static extern uint SymSetOptions(SymOptions SymOptions);

            /// <summary>
            /// Retrieves the specified source file from the source server.
            /// </summary>
            /// <param name="hProcess">A handle to a process. This handle must have been previously passed to the SymInitialize function.</param>
            /// <param name="Base">The base address of the module.</param>
            /// <param name="Params">This parameter is unused.</param>
            /// <param name="FileSpec">The name of the source file.</param>
            /// <param name="FilePath">A pointer to a buffer that receives the fully qualified path of the source file.</param>
            /// <param name="Size">The size of the FilePath buffer, in characters.</param>
            /// <remarks>
            /// BOOL WINAPI SymGetSourceFile(
            ///   _In_     HANDLE  hProcess,
            ///   _In_     ULONG64 Base,
            ///   _In_opt_ PCTSTR  Params,
            ///   _In_     PCTSTR  FileSpec,
            ///   _Out_    PTSTR   FilePath,
            ///   _In_     DWORD   Size
            /// );
            /// </remarks>
            [DllImport(DbgHelp, CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SymGetSourceFileW(IntPtr hProcess, long Base, IntPtr Params, string FileSpec, StringBuilder FilePath, int Size);

            /// <summary>
            /// Registers a callback function for use by the symbol handler.
            /// </summary>
            /// <remarks>
            /// BOOL WINAPI SymRegisterCallback(
            ///     _In_ HANDLE                        hProcess,
            ///     _In_ PSYMBOL_REGISTERED_CALLBACK   CallbackFunction,
            ///     _In_ PVOID                         UserContext
            /// );
            /// </remarks>
            [DllImport(DbgHelp, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SymRegisterCallback(IntPtr hProcess, [MarshalAs(UnmanagedType.FunctionPtr)]SymRegisterCallbackProc CallbackFunction, IntPtr UserContext);

            /// <summary>
            /// Loads the symbol table for the specified module.
            /// </summary>
            /// <param name="hProcess">A handle to the process that was originally passed to the SymInitialize function.</param>
            /// <param name="hFile">A handle to the file for the executable image. This argument is used mostly by debuggers, where the debugger passes the file handle obtained from a debugging event. A value of NULL indicates that hFile is not used.</param>
            /// <param name="ImageName">The name of the executable image. This name can contain a partial path, a full path, or no path at all. If the file cannot be located by the name provided, the symbol search path is used.</param>
            /// <param name="ModuleName">A shortcut name for the module. If the pointer value is NULL, the library creates a name using the base name of the symbol file.</param>
            /// <param name="BaseOfDll">The load address of the module. If the value is zero, the library obtains the load address from the symbol file. The load address contained in the symbol file is not necessarily the actual load address. Debuggers and other applications having an actual load address should use the real load address when calling this function.
            /// <br/>If the image is a .pdb file, this parameter cannot be zero.</param>
            /// <param name="SizeOfDll">The size of the module, in bytes. If the value is zero, the library obtains the size from the symbol file. The size contained in the symbol file is not necessarily the actual size. Debuggers and other applications having an actual size should use the real size when calling this function.
            /// <br/>If the image is a .pdb file, this parameter cannot be zero.</param>
            /// <remarks>DWORD64 WINAPI SymLoadModule64(
            ///     _In_     HANDLE  hProcess,
            ///     _In_opt_ HANDLE  hFile,
            ///     _In_opt_ PCSTR   ImageName,
            ///     _In_opt_ PCSTR   ModuleName,
            ///     _In_     DWORD64 BaseOfDll,
            ///     _In_     DWORD   SizeOfDll
            /// );
            /// </remarks>
            [DllImport(DbgHelp, CharSet = CharSet.Ansi, SetLastError = true)]
            public static extern long SymLoadModule64(IntPtr hProcess, IntPtr hFile, string ImageName, string ModuleName, long BaseOfDll, uint SizeOfDll);
        }
    }
}