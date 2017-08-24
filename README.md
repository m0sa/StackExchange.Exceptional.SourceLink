# Enabling Source-linked Stack Traces Like It's 2022

Enables deep linking to the exact source code line in an exception stack trace:

!https://pbs.twimg.com/media/DHkBHJ9WAAEiznA.jpg:large (via [@NickCraver](https://twitter.com/Nick_Craver/status/898750831448788992))

Currently supports
- classic .net PDBs with SRCSRV source-linked PDBs via [dbghelp.dll srcsrv.dll and symsrv.dll](https://msdn.microsoft.com/en-us/library/windows/desktop/ms679294.aspx) pinvoke

Planned:
- portable PDBs
- embedded (portable) PDBs
