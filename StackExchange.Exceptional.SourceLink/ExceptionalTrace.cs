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
        private static readonly SymRegisterCallbackProc RootedTraceDelegate = new SymRegisterCallbackProc(SymDebugCallback);
        private static readonly SymEnumSourceFilesCallback RootedEnumSourceFilesDelegate = new SymEnumSourceFilesCallback(SymEnumSourceFiles);

        private static readonly IntPtr ProcessHandle = Process.GetCurrentProcess().Handle;
        private static bool _trace;

        static ExceptionalTrace()
        {
            // init native, before dbghelp imports
            // make sure nothing from DbgHelp.dll gets called before this is set
            WINAPI(SetDllDirectory(Environment.Is64BitProcess ? "x64" : "x86"));
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
                Trace.WriteLine("Initializing, symbolsPath: " + symbolsPath);
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
                WINAPI(SymRegisterCallback(ProcessHandle, RootedTraceDelegate, IntPtr.Zero));
            }
        }

        /// <summary>
        /// Cleans up resources.
        /// </summary>
        public static void Shutdown()
        {
            WINAPI(SymCleanup(ProcessHandle));
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
            var sourceLinkHandle = rd
                .GetCustomDebugInformation(EntityHandle.ModuleDefinition)
                .Select(rd.GetCustomDebugInformation)
                .FirstOrDefault(cdi => rd.GetGuid(cdi.Kind) == SourceLinkId);
            var sourceLink = SourceLink.Deserialize(
                sourceLinkHandle.Value.IsNil
                    ? null
                    : rd.GetBlobBytes(sourceLinkHandle.Value));

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
                    // .net full doesn't spit out correct stack traces yet
                    // we'll reuse it later to generate them by ourself
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
                    // save get all source file mappings in this module
                    WINAPI(DbgHelpImports.SymEnumSourceFiles(ProcessHandle, result, null, RootedEnumSourceFilesDelegate, IntPtr.Zero));
                }
                finally
                {
                    // unload the project, don't hang on to it
                    WINAPI(SymUnloadModule64(ProcessHandle, result));
                }
            }
        }

        public class SourceLink
        {
            private static readonly DataContractJsonSerializer _serializer = new DataContractJsonSerializer(typeof(SourceLink), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            public static SourceLink Deserialize(byte[] blob)
            {
                if (blob == null || blob.Length == 0) return new SourceLink();
                using (var ms = new MemoryStream(blob))
                {
                    return (SourceLink)_serializer.ReadObject(ms);
                }
            }

            public IDictionary<string, string> documents { get; set; }

            public string GetUrl(string file)
            {
                if (documents == null || documents.Count == 0) return file;

                // https://github.com/ctaggart/SourceLink/blob/ef5fb54b063fc5d65d1b953b83d0154278e21e59/dotnet-sourcelink/Program.cs#L407
                foreach (var key in documents.Keys)
                {
                    if (key.Contains("*"))
                    {
                        var pattern = Regex.Escape(key).Replace(@"\*", "(?<path>.+)");
                        var regex = new Regex(pattern);
                        var m = regex.Match(file);
                        if (!m.Success) continue;
                        var url = documents[key];
                        var path = m.Groups["path"].Value.Replace(@"\", "/");
                        return url.Replace("*", path);
                    }
                    else
                    {
                        if (!key.Equals(file, StringComparison.Ordinal)) continue;
                        return documents[key];
                    }
                }

                return file;
            }
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
            if (WINAPI(SymGetSymbolFile(ProcessHandle, null, module.FullyQualifiedName, SymbolFileType.sfPdb, sbFile, sbFile.Capacity, sbPath, sbPath.Capacity)))
            {
                // todo determine if pdb is not portable early
                // https://github.com/tmat/corefx/blob/f808e59c3ef93e141b019d661a4443a0e19c7442/src/System.Diagnostics.StackTrace/src/System/Diagnostics/StackTraceSymbols.CoreCLR.cs#L164
                var pdbFileContent = File.ReadAllBytes(sbFile.ToString()).ToImmutableArray();
                TraceSourceLink("probing for portable PDB symbols: " + sbFile);
                return MetadataReaderProvider.FromPortablePdbImage(pdbFileContent);
            }

            return null;
        }

        [Conditional("TRACE")]
        private static void TraceSourceLink(string message)
        {
            if (!_trace) return;
            Trace.WriteLine(" SourceLink: " + message);
        }

        private static bool SymEnumSourceFiles(ref SourceFile sourceFile, IntPtr context)
        {
            var sb = new StringBuilder(1024);
            var sourcePath = sourceFile.FileName;
            if (SymGetSourceFile(ProcessHandle, sourceFile.ModBase, IntPtr.Zero, sourceFile.FileName, sb, sb.Capacity))
            {
                sourcePath = sb.ToString();
            }

            SourceMappedPaths[Tuple.Create(sourceFile.ModBase, sourceFile.FileName)] = sourcePath;

            return true;
        }

        private static bool SymDebugCallback(IntPtr hProcess, SymActionCode actionCode, IntPtr callbackData, IntPtr userContext)
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
            if (actionCode == SymActionCode.EVENT)
            {
                return false; // -> generate into DEBUG_INFO
            }
            var trace = new StringBuilder();
            trace.Append(DbgHelp)
#if DEBUG
                .Append(" " + ((long)ProcessHandle).ToString("X"))
                .Append(" " + ((long)hProcess).ToString("X"))
#endif
                .Append(": ").Append(actionCode);
            if (actionCode == SymActionCode.DEBUG_INFO)
            {
                trace.Append(" ").Append(Marshal.PtrToStringAnsi(callbackData)?.Trim());
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
}