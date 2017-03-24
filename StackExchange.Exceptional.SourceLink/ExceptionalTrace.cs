using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using static StackExchange.Exceptional.SourceLink.Native;

namespace StackExchange.Exceptional.SourceLink
{
    public static class ExceptionalTrace
    {
        // needz dbghelp.dll, srcsrv.dll, symsrv.dll in the bin directory

        // This must be rooted, the delegate creation is explicit, to show what's actually happening here.
        // If that happens inside a method (or by an implicit op) the SymRegisterCallbackProc64 instance gets picked up by the GC
        // See here for discussion: http://chat.meta.stackexchange.com/transcript/message/5829178#5829178
        // ReSharper disable once RedundantDelegateCreation
        private static readonly SymRegisterCallbackProc64 RootedTraceDelegate = new SymRegisterCallbackProc64(SymDebugCallback);
        private static readonly IntPtr ProcessHandle = Process.GetCurrentProcess().Handle;

        /// <summary>
        /// Initializes the native stack tracing hooks.
        /// </summary>
        /// <param name="trace">When set to true, prints diagnostic output in the Trace log when a debugger is attached.</param>
        /// <param name="symbolsPath">Sets user defined/additional <see href="https://msdn.microsoft.com/en-us/library/windows/desktop/ms680689.aspx">Symbol Paths</see>.</param>
        public static void Init(string symbolsPath = null, bool trace = false)
        {
            Shutdown();

            SymSetOptions(SymOptions.UNDNAME
                | SymOptions.DEFERRED_LOADS
                | SymOptions.LOAD_LINES
                | (trace ? SymOptions.DEBUG : 0)
                );

            WINAPI(SymInitialize(ProcessHandle, symbolsPath, false));
            if (trace)
            {
                // https://msdn.microsoft.com/en-us/library/windows/desktop/gg278179.aspx
                WINAPI(SymRegisterCallback64(ProcessHandle, RootedTraceDelegate, IntPtr.Zero));
            }
        }

        /// <summary>
        /// Cleans up resources.
        /// </summary>
        public static void Shutdown()
        {
            SymCleanup(ProcessHandle);
            SymLoadedModules.Clear();
            SourceMappedPaths.Clear();
        }

        /// <summary>
        /// Gets the error handler for hooking into <see cref="ErrorStore.OnBeforeLog" />, which replaces the stack trace in <see cref="Error.Detail" /> with a stack trace with SRCSRV mapped files.
        /// </summary>
        public static EventHandler<ErrorBeforeLogEventArgs> ErrorStoreBeforeLogHandler { get; } = ErrorStoreOnOnBeforeLog;

        private static void ErrorStoreOnOnBeforeLog(object sender, ErrorBeforeLogEventArgs args)
        {
            var exception = args.Error.Exception;
            if (exception == null) return;

            var fancyTraceBuilder = new StringBuilder();
            DumpExceptionStackTrace(fancyTraceBuilder, exception);

            if (fancyTraceBuilder.Length > 0)
            {
                args.Error.Detail = fancyTraceBuilder.ToString() +
#if DEBUG
                    Environment.NewLine + "----------- ORIGINAL -----------" +
                    Environment.NewLine + args.Error.Detail +
#endif
                    "";
                args.Error.ErrorHash = args.Error.GetHash();
            }
        }

        private static readonly Hashtable SymLoadedModules = Hashtable.Synchronized(new Hashtable());
        private static readonly object SymGetSorceFile_SyncRoot = new object();
        private static void DumpExceptionStackTrace(StringBuilder output, Exception ex)
        {
            // modeled after https://referencesource.microsoft.com/#mscorlib/system/exception.cs,9ce1ff20e283169f,references
            if (ex == null) return;

            output.Append(ex.GetType().FullName);
            if (!string.IsNullOrEmpty(ex.Message))
            {
                output.Append(": ").Append(ex.Message);
            }

            if (ex.InnerException != null)
            {
                output.Append(" ---> ");
                DumpExceptionStackTrace(output, ex.InnerException);
                output.AppendLine().Append("   --- End of inner exception stack trace ---");
            }

            var stackTrace = new StackTrace(ex, true);
            DumpStackTrace(output, stackTrace, "   at ");
        }

        private static readonly ConcurrentDictionary<Tuple<Module, string>, string> SourceMappedPaths = new ConcurrentDictionary<Tuple<Module, string>, string>();
        private static string SourceMap(Tuple<Module, string> moduleAndSourcePath)
        {
            var moduleBase = Marshal.GetHINSTANCE(moduleAndSourcePath.Item1);
            var sourcePath = moduleAndSourcePath.Item2;
            lock (SymGetSorceFile_SyncRoot)
            {
                var fileName = new StringBuilder(1000);
                if (SymGetSourceFile(ProcessHandle, moduleBase, "", sourcePath, fileName, fileName.Capacity))
                {
                    sourcePath = fileName.ToString();
                }
                return sourcePath;
            }
        }

        private static readonly ConcurrentDictionary<MethodBase, string> MethodSignatures = new ConcurrentDictionary<MethodBase, string>();
        private static string GetMethodSignature(MethodBase methodBase)
        {
            var output = new StringBuilder(1000);
            if (methodBase.DeclaringType != null)
            {
                output.Append(methodBase.DeclaringType?.FullName).Append(".");
            }

            output
                .Append(methodBase.Name)
                .Append("(");

            var delimiter = "";
            var parameters = methodBase.GetParameters() ?? new ParameterInfo[0];
            for (var p = 0; p < parameters.Length; p++)
            {
                var param = parameters[p];
                output
                    .Append(delimiter)
                    .Append(param.ParameterType.Name)
                    .Append(" ")
                    .Append(param.Name);

                delimiter = ", ";
            }
            output.Append(")");
            return output.ToString();
        }

        public static void SourceMappedTrace(this Exception ex, StringBuilder output) => DumpExceptionStackTrace(output, ex);

        public static void SourceMappedTrace(this StackTrace stackTrace, StringBuilder output) => DumpStackTrace(output, stackTrace, "   at ");

        public static string SourceMappedTrace(this Exception ex)
        {
            var sb = new StringBuilder();
            ex.SourceMappedTrace(sb);
            return sb.ToString();
        }

        public static string SourceMappedTrace(this StackTrace trace)
        {
            var sb = new StringBuilder();
            trace.SourceMappedTrace(sb);
            return sb.ToString();
        }

        private static void DumpStackTrace(StringBuilder output, StackTrace stackTrace, string framePrefix, int skip = 0)
        {
            var frames = stackTrace.GetFrames() ?? new StackFrame[0];
            for (var f = skip; f < frames.Length; f++)
            {
                var frame = frames[f];
                var methodBase = frame.GetMethod();
                var module = methodBase.Module;

                if (!module.Assembly.IsDynamic && !SymLoadedModules.ContainsKey(module))
                {
                    lock (SymLoadedModules.SyncRoot)
                    {
                        if (!SymLoadedModules.ContainsKey(module))
                        {
                            // load symbols
                            var moduleBase = Marshal.GetHINSTANCE(module);
                            SymLoadModule64(ProcessHandle, IntPtr.Zero, module.FullyQualifiedName, module.Name, moduleBase, 0);
                            SymLoadedModules.Add(module, null);
                        }
                    }
                }

                output
                    .AppendLine()
                    .Append(framePrefix)
                    .Append(MethodSignatures.GetOrAdd(methodBase, key => GetMethodSignature(key)));

                var resolvedFileName = frame.GetFileName();
                if (!string.IsNullOrEmpty(resolvedFileName))
                {
                    resolvedFileName = SourceMappedPaths.GetOrAdd(Tuple.Create(module, resolvedFileName), key => SourceMap(key));
                    output
                        .Append(" in ")
                        .Append(resolvedFileName)
                        .Append(":line ")
                        .Append(frame.GetFileLineNumber());
                }

                // TODO GetIsLastFrameFromForeignExceptionStackTrace()
            }
        }

        private static bool SymDebugCallback(IntPtr hProcess, SymActionCode actionCode, IntPtr callbackData, IntPtr userContext)
        {
            if (!Debugger.IsAttached)
            {
                return false;
            }

            if (actionCode == SymActionCode.EVENT)
            {
                return false; // -> generate into DEBUG_INFO
            }

            var trace = new StringBuilder();
            trace.Append(DbgHelp).Append(": ").Append(actionCode);
            if (actionCode == SymActionCode.DEBUG_INFO)
            {
                trace.Append(" ").Append(Marshal.PtrToStringAnsi(callbackData)?.Trim());
            }

            Trace.WriteLine(trace.ToString());

            return false;
        }
    }

}