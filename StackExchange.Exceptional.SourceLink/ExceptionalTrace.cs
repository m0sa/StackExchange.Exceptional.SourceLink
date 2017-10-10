using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using static StackExchange.Exceptional.SourceLink.Native;
using static StackExchange.Exceptional.SourceLink.Native.DbgHelpImports;

namespace StackExchange.Exceptional.SourceLink
{
    public static class ExceptionalTrace
    {
        // needz dbghelp.dll, srcsrv.dll, symsrv.dll in the bin directory

        // This must be rooted, the delegate creation is explicit, to show what's actually happening here.
        // If that happens inside a method (or by an implicit op) the SymRegisterCallbackProc instance gets picked up by the GC
        // See here for discussion: http://chat.meta.stackexchange.com/transcript/message/5829178#5829178
        // ReSharper disable once RedundantDelegateCreation
        private static readonly SymRegisterCallbackProcW64 RootedTraceDelegate = new SymRegisterCallbackProcW64(SymDebugCallback);
        private static readonly SymEnumSourceFilesCallbackW RootedEnumSourceFilesDelegate = new SymEnumSourceFilesCallbackW(SymEnumSourceFiles);

        private static readonly IntPtr ProcessHandle = Process.GetCurrentProcess().Handle;
        private static bool _trace;
        private static readonly LibHandle _dbgHelpHandle;

        static ExceptionalTrace()
        {
            // * make sure nothing from DbgHelp.dll gets called before this is set
            // * we need to load the dbghelp.dll and it's dependencies via the alternate search order (also affects all dependecies)
            //   https://msdn.microsoft.com/en-us/library/windows/desktop/ms682586.aspx#Alternate_Search_Order_for_Desktop_Applications
            //   otherwise dbghelp gets picked up from system32, srcsrv and symsrv don't get loaded and you get 0x7e errors
            // * kernel32!SetDllDirectory("x64") works on desktop apps, but fails horribly in IIS                ^^
            var codeBase = Assembly.GetExecutingAssembly().CodeBase;
            var location = new Uri(codeBase).LocalPath;
            var directory = Path.GetDirectoryName(location);
            var dbgHelpPath = Path.Combine(directory, Environment.Is64BitProcess ? "x64" : "x86", DbgHelp);

            _dbgHelpHandle = new LibHandle(dbgHelpPath, LoadLibraryFlags.LOAD_WITH_ALTERED_SEARCH_PATH);
        }

        /// <summary>
        /// Initializes the native stack tracing hooks.
        /// </summary>
        /// <param name="trace">When set to true, prints diagnostic output in the Trace log when a debugger is attached.</param>
        /// <param name="symbolsPath">Sets user defined/additional <see href="https://msdn.microsoft.com/en-us/library/windows/desktop/ms680689.aspx">Symbol Paths</see>.</param>
        public static void Init(string symbolsPath = null, bool trace = false)
        {
            _trace = trace;
            if (trace)
            {
                TraceSourceLink("Initializing, symbolsPath: " + symbolsPath);
            }

            SymSetOptions(SymOptions.UNDNAME
                | SymOptions.DEFERRED_LOADS
                | SymOptions.LOAD_LINES
                | (trace ? SymOptions.DEBUG : 0)
                );

            WINAPI(SymInitialize(ProcessHandle, symbolsPath, false));
            if (trace)
            {
                // https://msdn.microsoft.com/en-us/library/windows/desktop/gg278179.aspx
                WINAPI(SymRegisterCallbackW64(ProcessHandle, RootedTraceDelegate, 0));
            }
        }

        /// <summary>
        /// Cleans up resources.
        /// </summary>
        public static void Shutdown()
        {
            lock(SymLoadedModules.SyncRoot)
            {
                WINAPI(SymCleanup(ProcessHandle));
                foreach(var disposable in SymLoadedModules.OfType<IDisposable>())
                {
                    disposable.Dispose();
                }
                SymLoadedModules.Clear();
            }
            SourceMappedPaths.Clear();
        }

        /// <summary>
        /// Gets the error handler for hooking into <see cref="ErrorStore.OnBeforeLog" />, which replaces the stack trace in <see cref="Error.Detail" /> with a stack trace with SRCSRV mapped files.
        /// </summary>
        public static EventHandler<ErrorBeforeLogEventArgs> ErrorStoreBeforeLogHandler { get; } = ErrorStoreOnOnBeforeLog;

        private static void ErrorStoreOnOnBeforeLog(object sender, ErrorBeforeLogEventArgs args)
        {
            var exception = args.Error.Exception;
            if (exception == null || exception is ExceptionalTraceException) return;

            var fancyTraceBuilder = new StringBuilder();
            try
            {
                DumpExceptionStackTrace(fancyTraceBuilder, exception);
            }
            catch (Exception ex)
            {
                if (sender is ErrorStore store)
                {
                    store.Log(new Error(new ExceptionalTraceException("ExceptionalTrace failed", ex)
                    {
                        Data = { { "GeneratedBeforeException", fancyTraceBuilder.ToString() } },
                    }));
                }
                fancyTraceBuilder.Clear();
            }

            if (fancyTraceBuilder.Length > 0)
            {
                args.Error.Detail = fancyTraceBuilder.ToString()
#if DEBUG
                +
                    Environment.NewLine + "----------- ORIGINAL -----------" +
                    Environment.NewLine + args.Error.Detail
#endif
                    ;
                args.Error.ErrorHash = args.Error.GetHash();
            }
        }

        private static readonly Hashtable SymLoadedModules = Hashtable.Synchronized(new Hashtable());
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
            DumpStackTrace(output, stackTrace);
        }

        private static readonly ConcurrentDictionary<Tuple<long, string>, string> SourceMappedPaths = new ConcurrentDictionary<Tuple<long, string>, string>();

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

        public static void SourceMappedTrace(this StackTrace stackTrace, StringBuilder output) => DumpStackTrace(output, stackTrace);

        public static string SourceMappedTrace(this Exception ex)
        {
            var sb = new StringBuilder();
            DumpExceptionStackTrace(sb, ex);
            return sb.ToString();
        }

        public static string SourceMappedTrace(this StackTrace trace)
        {
            var sb = new StringBuilder();
            DumpStackTrace(sb, trace);
            return sb.ToString();
        }

        private static void DumpStackTrace(StringBuilder output, StackTrace stackTrace, string framePrefix = "   at ", int skip = 0)
        {
            var frames = stackTrace.GetFrames() ?? new StackFrame[0];
            for (var f = skip; f < frames.Length; f++)
            {
                var frame = frames[f];
                var methodBase = frame.GetMethod();
                var module = methodBase.Module;

                output
                    .AppendLine()
                    .Append(framePrefix)
                    .Append(MethodSignatures.GetOrAdd(methodBase, key => GetMethodSignature(key)));

                var resolvedFileName = frame.GetFileName();
                var resolvedFileLine = frame.GetFileLineNumber();
                ResolveAllPathsInModule(module);

                if (string.IsNullOrEmpty(resolvedFileName) && SymLoadedModules[module] is MetadataReaderProvider mrp)
                {
                    GetSourceLineInfo(mrp.GetMetadataReader(), methodBase.MetadataToken, frame.GetILOffset(), out resolvedFileName, out resolvedFileLine, out int _);
                }

                if (!string.IsNullOrEmpty(resolvedFileName))
                {
                    resolvedFileName =
                        SourceMappedPaths.TryGetValue(Tuple.Create((long)Marshal.GetHINSTANCE(module), resolvedFileName), out var mappedFileName)
                            ? mappedFileName
                            : resolvedFileName;
                    output
                        .Append(" in ")
                        .Append(resolvedFileName)
                        .Append(":line ")
                        .Append(resolvedFileLine);
                }

                // TODO GetIsLastFrameFromForeignExceptionStackTrace()
            }
        }

        public static readonly Guid SourceLinkId = new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A");

        private static bool TryMapPortablePdb(Module module, out MetadataReaderProvider metadataReaderProvider)
        {
            metadataReaderProvider = null;
            MetadataReader rd = null;
            try
            {
                metadataReaderProvider = GetMetadataReaderProvider(module);
                rd = metadataReaderProvider?.GetMetadataReader();
            }
            catch (BadImageFormatException ex) when (ex.Message == "Invalid COR20 header signature.")
            {
                TraceSourceLink("no portable PDB found: " + module.FullyQualifiedName);
                // todo figure out a better way to detect if PDB is portable or classic
                // https://github.com/dotnet/corefx/blob/06b1365d9881ed26a921490d7edd2d4e4de35565/src/System.Reflection.Metadata/src/System/Reflection/Metadata/MetadataReader.cs#L185
            }
            if (rd == null)
            {
                metadataReaderProvider?.Dispose();
                metadataReaderProvider = null;
                return false;
            }

            TraceSourceLink("found portable PDB for: " + module.FullyQualifiedName);

            // https://github.com/dotnet/symreader-portable/blob/d27c08d6015c4716ced790e34233c0219773ab10/src/Microsoft.DiaSymReader.PortablePdb/Utilities/MetadataUtilities.cs
            var sourceLinkHandles = rd
                .GetCustomDebugInformation(EntityHandle.ModuleDefinition)
                .Where(r => !r.IsNil)
                .Select(rd.GetCustomDebugInformation)
                .Where(cdi => !cdi.Value.IsNil && rd.GetGuid(cdi.Kind) == SourceLinkId)
                .ToList();
            if (sourceLinkHandles.Count == 0)
            {
                metadataReaderProvider?.Dispose();
                metadataReaderProvider = null;
                return false;
            }
            var sourceLinkHandle = sourceLinkHandles.First();
            var sourceLink = SourceLink.Deserialize(rd.GetBlobBytes(sourceLinkHandle.Value));

            var hinstance = (long)Marshal.GetHINSTANCE(module);
            foreach (var dh in rd.Documents)
            {
                if (dh.IsNil) continue;
                var doc = rd.GetDocument(dh);

                var file = rd.GetString(doc.Name);
                SourceMappedPaths[Tuple.Create(hinstance, file)] = sourceLink.GetUrl(file);
            }

            return true;
        }

        private static void ResolveAllPathsInModule(Module module)
        {
            if (module.Assembly.IsDynamic || SymLoadedModules.ContainsKey(module))
            {
                return;
            }

            lock (SymLoadedModules.SyncRoot)
            {
                if (SymLoadedModules.ContainsKey(module))
                {
                    return;
                }

                if (TryMapPortablePdb(module, out var metadataReaderProvider))
                {
                    // store the metadata reader provider for later,
                    // .net full bellow 4.7 doesn't spit out correct frame
                    // line info from portable and embedded pdbs yet
                    // we'll reuse it later to figure them out by ourself
                    SymLoadedModules[module] = metadataReaderProvider;

                    // we've successfully read a portable PDB
                    // no need to pinvoke dbghelp ...
                    return;
                }

                // load symbols
                var hinstance = Marshal.GetHINSTANCE(module);
                var result = SymLoadModule64(ProcessHandle, IntPtr.Zero, module.FullyQualifiedName, module.Name, (long)hinstance, 0);
                if (result == 0)
                {
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                }

                SymLoadedModules[module] = result;

                try
                {
                    TraceSourceLink("{0} - enumerating source files", module.Name);
                    // save get all source file mappings in this module
                    WINAPI(SymEnumSourceFilesW(ProcessHandle, result, null, RootedEnumSourceFilesDelegate, IntPtr.Zero));
                }
                catch (Exception ex)
                {
                    TraceSourceLink("{0} - SymEnumSourceFiles failed - {1}", module.Name, ex);
                    throw;
                }
                finally
                {
                    WINAPI(SymUnloadModule64(ProcessHandle, result));
                }
            }
        }

        struct MappingAttempt
        {
            public MappingAttempt(bool success, string mappedValue)
            {
                Success = success;
                MappedValue = mappedValue;
            }

            public bool Success { get; }
            public string MappedValue { get; }
        }

        [DataContract]
        public class SourceLink
        {
            private static readonly DataContractJsonSerializer _serializer = new DataContractJsonSerializer(typeof(SourceLink), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            public static SourceLink Deserialize(byte[] blob)
            {
                if (blob == null || blob.Length == 0) return new SourceLink();
                using (var ms = new MemoryStream(blob))
                {
                    var sourceLink = (SourceLink)_serializer.ReadObject(ms);
                    sourceLink?.Initialize();
                    return sourceLink;
                }
            }

            [DataMember(Name = "documents")]
            public IDictionary<string, string> Documents { get; private set; }

            private List<Func<string, MappingAttempt>> _mappers;

            private void Initialize()
            {
                if (Documents == null || Documents.Count == 0) return;

                var mappers = new List<Func<string, MappingAttempt>>();
                // https://github.com/ctaggart/SourceLink/blob/ef5fb54b063fc5d65d1b953b83d0154278e21e59/dotnet-sourcelink/Program.cs#L407
                foreach (var kvp in Documents)
                {
                    var from = kvp.Key;
                    var to = kvp.Value;
                    if (from.Contains("*") && to.Contains("*"))
                    {
                        var pattern = Regex.Escape(from).Replace(@"\*", "(?<path>.+)");
                        var matcher = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                        mappers.Add(file => matcher.Match(file) is Match m && m.Success
                            ? new MappingAttempt(true, to.Replace("*", m.Groups["path"].Value.Replace(@"\", "/")))
                            : new MappingAttempt(false, null));
                    }
                    else
                    {
                        mappers.Add(file => from.Equals(file, StringComparison.OrdinalIgnoreCase)
                            ? new MappingAttempt(true, to)
                            : new MappingAttempt(false, null));
                    }
                }
                _mappers = mappers;
            }

            public string GetUrl(string file) =>
                _mappers
                    .Select(tryMap => tryMap(file))
                    .FirstOrDefault(m => m.Success) is var result
                        ? result.MappedValue
                        : file;
        }

        private static MetadataReaderProvider GetMetadataReaderProvider(Module module)
        {
            // TODO can the PEReader be pointed at a memory location instead of reading from disk?
            var dllFileContent = File.ReadAllBytes(module.FullyQualifiedName).ToImmutableArray();
            var reader = new PEReader(dllFileContent);
            TraceSourceLink("probing for embedded symbols: " + module.FullyQualifiedName);
            if (!reader.HasMetadata) return null;

            // https://github.com/dotnet/corefx/blob/master/src/System.Reflection.Metadata/tests/PortableExecutable/PEReaderTests.cs
            var embeddedPdb = reader.ReadDebugDirectory().FirstOrDefault(x => x.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
            if (!embeddedPdb.Equals(default(DebugDirectoryEntry)))
            {
                TraceSourceLink("found embedded symbols: " + module.FullyQualifiedName);
                return reader.ReadEmbeddedPortablePdbDebugDirectoryData(embeddedPdb);
            }

            var sbFile = new StringBuilder(1024);
            var sbPath = new StringBuilder(1024);
            if (SymGetSymbolFile(ProcessHandle, null, module.FullyQualifiedName, SymbolFileType.sfPdb, sbFile, sbFile.Capacity, sbPath, sbPath.Capacity))
            {
                // todo determine if pdb is not portable early
                // https://github.com/tmat/corefx/blob/f808e59c3ef93e141b019d661a4443a0e19c7442/src/System.Diagnostics.StackTrace/src/System/Diagnostics/StackTraceSymbols.CoreCLR.cs#L164
                TraceSourceLink("probing for portable PDB symbols: " + sbFile);
                var pdbFileContent = File.ReadAllBytes(sbFile.ToString()).ToImmutableArray();
                return MetadataReaderProvider.FromPortablePdbImage(pdbFileContent);
            }
            else
            {
                TraceSourceLink("probing for portable PDB symbols failed: " + Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }

            return null;
        }

        [Conditional("TRACE")]
        private static void TraceSourceLink(string message)
        {
            if (!_trace) return;
            Trace.WriteLine(" SourceLink: " + message);
        }

        [Conditional("TRACE")]
        private static void TraceSourceLink(string messageFormat, params object[] args)
        {
            if (!_trace) return;
            Trace.WriteLine(" SourceLink: " + string.Format(messageFormat, args));
        }

        private static bool SymEnumSourceFiles(ref SourceFileW sourceFile, IntPtr context)
        {
            var sb = new StringBuilder(1024);
            var sourcePath = sourceFile.FileName;
            if (SymGetSourceFileW(ProcessHandle, sourceFile.ModBase, IntPtr.Zero, sourceFile.FileName, sb, sb.Capacity))
            {
                sourcePath = sb.ToString();
            }

            SourceMappedPaths[Tuple.Create(sourceFile.ModBase, sourceFile.FileName)] = sourcePath;
            return true;
        }

        private static bool SymDebugCallback(IntPtr hProcess, SymActionCode actionCode, long callbackData, long userContext)
        {
            if (!_trace)
            {
                return false;
            }
            if (!Environment.Is64BitProcess)
            {
                // WTF?! the only way I managed to get it to work correctly..
                // calling conventions suck
                hProcess = new IntPtr((long)actionCode & uint.MaxValue);
                actionCode = (SymActionCode)((long)actionCode >> 32);
            }
            var trace = new StringBuilder();
            trace.Append(DbgHelp)
#if DEBUG
                .Append(" " + ((long)ProcessHandle).ToString("X"))
                .Append(" " + ((long)hProcess).ToString("X"))
#endif
                .Append(": ").Append(actionCode);
            if (actionCode.HasFlag(SymActionCode.SRCSRV_INFO))
            {
                trace.Append(" ").Append(Marshal.PtrToStringAuto(new IntPtr(callbackData))?.Trim());
                return true;
            }
            else if (actionCode.HasFlag(SymActionCode.EVENT) || actionCode.HasFlag(SymActionCode.SRCSRV_EVENT))
            {
                var evt = (CBA_EVENT)Marshal.PtrToStructure(new IntPtr(callbackData), typeof(CBA_EVENT));
                trace.Append("[").Append(evt.severity).Append("] ").Append(evt.desc?.Trim());
                Trace.WriteLine(trace.ToString());
                return true;
            }
            else if (actionCode == SymActionCode.DEBUG_INFO)
            {
                trace.Append(" ").Append(Marshal.PtrToStringUni(new IntPtr(callbackData))?.Trim());
            }

            Trace.WriteLine(trace.ToString());
            return false;
        }

        // https://github.com/dotnet/corefx/blob/9802644d90aa7fe6aba6d621724a307212303d08/src/System.Diagnostics.StackTrace.Symbols/src/System/Diagnostics/StackTrace/Symbols.cs
        private static void GetSourceLineInfo(MetadataReader reader, int methodToken, int ilOffset, out string sourceFile, out int sourceLine, out int sourceColumn)
        {
            sourceFile = null;
            sourceLine = 0;
            sourceColumn = 0;

            if (reader != null)
            {
                Handle handle = MetadataTokens.Handle(methodToken);

                if (handle.Kind == HandleKind.MethodDefinition)
                {
                    MethodDebugInformationHandle methodDebugHandle = ((MethodDefinitionHandle)handle).ToDebugInformationHandle();
                    MethodDebugInformation methodInfo = reader.GetMethodDebugInformation(methodDebugHandle);

                    if (!methodInfo.SequencePointsBlob.IsNil)
                    {
                        try
                        {
                            SequencePointCollection sequencePoints = methodInfo.GetSequencePoints();

                            int sequencePointCount = 0;
                            foreach (SequencePoint sequence in sequencePoints)
                            {
                                sequencePointCount++;
                            }

                            if (sequencePointCount > 0)
                            {
                                int[] offsets = new int[sequencePointCount];
                                int[] lines = new int[sequencePointCount];
                                int[] columns = new int[sequencePointCount];
                                DocumentHandle[] documents = new DocumentHandle[sequencePointCount];

                                int i = 0;
                                foreach (SequencePoint sequence in sequencePoints)
                                {
                                    offsets[i] = sequence.Offset;
                                    lines[i] = sequence.StartLine;
                                    columns[i] = sequence.StartColumn;
                                    documents[i] = sequence.Document;
                                    i++;
                                }

                                // Search for the correct IL offset
                                int j;
                                for (j = 0; j < sequencePointCount; j++)
                                {

                                    // look for the entry matching the one we're looking for
                                    if (offsets[j] >= ilOffset)
                                    {

                                        // if this offset is > what we're looking for, ajdust the index
                                        if (offsets[j] > ilOffset && j > 0)
                                        {
                                            j--;
                                        }
                                        break;
                                    }
                                }

                                // If we didn't find a match, default to the last sequence point
                                if (j == sequencePointCount)
                                {
                                    j--;
                                }

                                while (lines[j] == SequencePoint.HiddenLine && j > 0)
                                {
                                    j--;
                                }

                                if (lines[j] != SequencePoint.HiddenLine)
                                {
                                    sourceLine = lines[j];
                                    sourceColumn = columns[j];
                                }
                                var doc = reader.GetDocument(documents[j]);
                                sourceFile = reader.GetString(doc.Name);
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

    }

    public class ExceptionalTraceException : Exception
    {
        public ExceptionalTraceException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}