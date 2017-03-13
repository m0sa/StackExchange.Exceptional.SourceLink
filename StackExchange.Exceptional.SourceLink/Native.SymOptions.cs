using System;

namespace StackExchange.Exceptional.SourceLink
{
    /// <summary>
    /// https://msdn.microsoft.com/en-us/library/windows/hardware/ff558827.aspx
    /// </summary>
    [Flags]
    public enum SymOptions : uint
    {
        /// <summary>
        /// All symbol searches are insensitive to case.
        /// </summary>
        CASE_INSENSITIVE = 0x00000001,

        /// <summary>
        /// All symbols are presented in undecorated form. This option has no effect on global or local symbols because they are stored undecorated. This option applies only to public symbols.
        /// </summary>
        UNDNAME = 0x00000002,

        /// <summary>
        /// Symbols are not loaded until a reference is made requiring the symbols be loaded. This is the fastest, most efficient way to use the symbol handler.
        /// </summary>
        DEFERRED_LOADS = 0x00000004,

        /// <summary>
        /// All C++ decorated symbols containing the symbol separator "::" are replaced by "__". This option exists for debuggers that cannot handle parsing real C++ symbol names.
        /// </summary>
        NO_CPP = 0x00000008,

        /// <summary>
        /// Loads line number information.
        /// </summary>
        LOAD_LINES = 0x00000010,

        /// <summary>
        /// When code has been optimized and there is no symbol at the expected location, this option causes the nearest symbol to be used instead.
        /// </summary>
        OMAP_FIND_NEAREST = 0x00000020,

        /// <summary>
        /// Disable checks to ensure a file (.exe, .dbg., or .pdb) is the correct file. Instead, load the first file located.
        /// </summary>
        LOAD_ANYTHING = 0x00000040,

        /// <summary>
        /// Ignore path information in the CodeView record of the image header when loading a .pdb file.
        /// </summary>
        IGNORE_CVREC = 0x00000080,

        /// <summary>
        /// Prevents symbols from being loaded when the caller examines symbols across multiple modules. Examine only the module whose symbols have already been loaded.
        /// </summary>
        NO_UNQUALIFIED_LOADS = 0x00000100,

        /// <summary>
        /// Do not display system dialog boxes when there is a media failure such as no media in a drive. Instead, the failure happens silently.
        /// </summary>
        FAIL_CRITICAL_ERRORS = 0x00000200,

        /// <summary>
        /// Do not load an unmatched .pdb file. Do not load export symbols if all else fails.
        /// </summary>
        EXACT_SYMBOLS = 0x00000400,

        /// <summary>
        /// Enables the use of symbols that are stored with absolute addresses. Most symbols are stored as RVAs from the base of the module. DbgHelp translates them to absolute addresses. There are symbols that are stored as an absolute address. These have very specialized purposes and are typically not used.
        /// DbgHelp 5.1 and earlier:  This value is not supported.
        /// </summary>
        ALLOW_ABSOLUTE_SYMBOLS = 0x00000800,

        /// <summary>
        /// Do not use the path specified by _NT_SYMBOL_PATH if the user calls SymSetSearchPath without a valid path.
        /// DbgHelp 5.1:  This value is not supported.
        /// </summary>
        IGNORE_NT_SYMPATH = 0x00001000,

        /// <summary>
        /// When debugging on 64-bit Windows, include any 32-bit modules.
        /// </summary>
        INCLUDE_32BIT_MODULES = 0x00002000,

        /// <summary>
        /// Do not use private symbols. The version of DbgHelp that shipped with earlier Windows release supported only public symbols; this option provides compatibility with this limitation.
        /// DbgHelp 5.1:  This value is not supported.
        /// </summary>
        PUBLICS_ONLY = 0x00004000,

        /// <summary>
        /// Do not search the publics table for symbols. This option should have little effect because there are copies of the public symbols in the globals table.
        /// DbgHelp 5.1:  This value is not supported.
        /// </summary>
        NO_PUBLICS = 0x00008000,

        /// <summary>
        /// Do not search the public symbols when searching for symbols by address, or when enumerating symbols, unless they were not found in the global symbols or within the current scope. This option has no effect with PUBLICS_ONLY.
        /// DbgHelp 5.1 and earlier:  This value is not supported.
        /// </summary>
        AUTO_PUBLICS = 0x00010000,

        /// <summary>
        /// Do not search the image for the symbol path when loading the symbols for a module if the module header cannot be read.
        /// DbgHelp 5.1:  This value is not supported.
        /// </summary>
        NO_IMAGE_SEARCH = 0x00020000,

        /// <summary>
        /// DbgHelp will not load any symbol server other than SymSrv. SymSrv will not use the downstream store specified in _NT_SYMBOL_PATH. After this flag has been set, it cannot be cleared.
        /// DbgHelp 6.0 and 6.1:  This flag can be cleared.
        /// DbgHelp 5.1:  This value is not supported.
        /// </summary>
        SECURE = 0x00040000,

        /// <summary>
        /// Pass debug output through OutputDebugString or the SymRegisterCallbackProc64 callback function.
        /// </summary>
        DEBUG = 0x80000000
    }
}