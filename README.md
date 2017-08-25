# Enabling Source-linked Stack Traces Like It's 2022

Enables deep linking of a stack frame to the exact source code line that caused it (which was most probably recorded in the past / not on `master/HEAD`):

![Source-linked stack trace in opserver](https://pbs.twimg.com/media/DHkBHJ9WAAEiznA.jpg:large) (via [@NickCraver](https://twitter.com/Nick_Craver/status/898750831448788992))

Currently supports
- classic .net PDBs with SRCSRV source-linked PDBs via [dbghelp.dll srcsrv.dll and symsrv.dll](https://msdn.microsoft.com/en-us/library/windows/desktop/ms679294.aspx) pinvoke

Planned:
- portable PDBs
- embedded (portable) PDBs

Inspired by:
- [ctaggart/SourceLink](https://github.com/ctaggart/SourceLink)
- [aarnott/PdbGit](https://github.com/aarnott/PdbGit)
- [NickCraver/StackExchange.Exceptional](https://github.com/NickCraver/StackExchange.Exceptional)
- [opserver/Opserver](https://github.com/opserver/Opserver)
