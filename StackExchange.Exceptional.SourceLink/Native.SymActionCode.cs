using System;

namespace StackExchange.Exceptional.SourceLink
{
    partial class Native {
        /// <summary>
        /// ActionCodes forwarded to <see cref="DbgHelpInterop.SymRegisterCallbackProc64"/>.
        /// </summary>
        [Flags]
        public enum SymActionCode : uint
        {
            /// <summary>
            /// Display verbose information.
            /// The CallbackData parameter is a pointer to a string.
            /// </summary>
            DEBUG_INFO = 0x10000000,

            /// <summary>
            /// Deferred symbol loading has started. To cancel the symbol load, return TRUE.
            /// The CallbackData parameter is a pointer to a IMAGEHLP_DEFERRED_SYMBOL_LOAD64 structure.
            /// </summary>
            DEFERRED_SYMBOL_LOAD_CANCEL = 0x00000007,
            /// <summary>
            /// Deferred symbol load has completed. 
            /// The CallbackData parameter is a pointer to a IMAGEHLP_DEFERRED_SYMBOL_LOAD64 structure.
            /// </summary>
            DEFERRED_SYMBOL_LOAD_COMPLETE = 0x00000002,
            /// <summary>
            /// Deferred symbol load has failed.
            /// The CallbackData parameter is a pointer to a IMAGEHLP_DEFERRED_SYMBOL_LOAD64 structure. The symbol handler will attempt to load the symbols again if the callback function sets the FileName member of this structure.
            /// </summary>
            DEFERRED_SYMBOL_LOAD_FAILURE = 0x00000003,

            /// <summary>
            /// Deferred symbol load has partially completed. The symbol loader is unable to read the image header from either the image file or the specified module.
            /// The CallbackData parameter is a pointer to a IMAGEHLP_DEFERRED_SYMBOL_LOAD64 structure. The symbol handler will attempt to load the symbols again if the callback function sets the FileName member of this structure.
            /// DbgHelp 5.1:  This value is not supported.
            /// </summary>
            DEFERRED_SYMBOL_LOAD_PARTIAL = 0x00000020,

            /// <summary>
            /// Deferred symbol load has started.
            /// The CallbackData parameter is a pointer to a IMAGEHLP_DEFERRED_SYMBOL_LOAD64 structure.
            /// </summary>
            DEFERRED_SYMBOL_LOAD_START = 0x00000001,

            /// <summary>
            /// Duplicate symbols were found. This reason is used only in COFF or CodeView format.
            /// The CallbackData parameter is a pointer to a IMAGEHLP_DUPLICATE_SYMBOL64 structure. To specify which symbol to use, set the SelectedSymbol member of this structure.
            /// </summary>
            DUPLICATE_SYMBOL = 0x00000005,

            /// <summary>
            /// Display verbose information. If you do not handle this event, the information is resent through the CBA_DEBUG_INFO event.
            /// The CallbackData parameter is a pointer to a IMAGEHLP_CBA_EVENT structure.
            /// </summary>
            EVENT = 0x00000010,

            /// <summary>
            /// The loaded image has been read.
            /// The CallbackData parameter is a pointer to a IMAGEHLP_CBA_READ_MEMORY structure. The callback function should read the number of bytes specified by the bytes member into the buffer specified by the buf member, and update the bytesread member accordingly.
            /// </summary>
            READ_MEMORY = 0x00000006,

            /// <summary>
            /// Symbol options have been updated. To retrieve the current options, call the SymGetOptions function.
            /// The CallbackData parameter should be ignored.
            /// </summary>
            SET_OPTIONS = 0x00000008,

            /// <summary>
            /// Display verbose information for source server. If you do not handle this event, the information is resent through the CBA_DEBUG_INFO event.
            /// The CallbackData parameter is a pointer to a IMAGEHLP_CBA_EVENT structure.
            /// DbgHelp 6.6 and earlier:  This value is not supported.
            /// </summary>
            SRCSRV_EVENT = 0x40000000,

            /// <summary>
            /// Display verbose information for source server.
            /// The CallbackData parameter is a pointer to a string.
            /// DbgHelp 6.6 and earlier:  This value is not supported.
            /// </summary>
            SRCSRV_INFO = 0x20000000,

            /// <summary>
            /// Symbols have been unloaded.
            /// The CallbackData parameter should be ignored.
            /// </summary>
            SYMBOLS_UNLOADED = 0x00000004,
        }
    }
}