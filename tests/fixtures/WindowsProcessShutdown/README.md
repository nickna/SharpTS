# Windows unhandled-error shutdown (#1772)

Both displayed originals and complete Node expectations are preserved. Fresh
unchanged main and pre-repair current reproduce the historical 30-second timeout
under the restricted Windows launcher in both normal and CreateNoWindow modes.
They print the original unsupported Function/missing-runtime errors, then remain
alive. Forced kills are failures; pipes close promptly after termination.

The same verified assembly exits in about 100 ms under the ordinary launcher. A
minimal .NET console (`Console.WriteLine("before"); throw new
InvalidOperationException("unhandled-control");`) also times out in the restricted
context and exits promptly outside it. Applying SEM_NOGPFAULTERRORBOX makes that
control finish in 56–60 ms in the restricted context. This controlled comparison
isolates the Windows fatal-error reporting path rather than compiler iterator or
pipe-capture behavior. See Microsoft's [SetErrorMode documentation](https://learn.microsoft.com/en-us/windows/win32/api/errhandlingapi/nf-errhandlingapi-seterrormode).

Generated executables now preserve inherited Windows error-mode flags and add
that reporting policy when stderr is redirected and the generated assembly owns
the process entry point. Interactive processes and embedding hosts retain their
policy. Windows control imports use the existing persisted-metadata seam; the
native compiler does not marshal them. Platforms other than Windows skip them.

Repaired saved originals finish naturally in 65–130 ms in the restricted launch
context and drain both pipes. Original error text, stack traces and nonzero CLR
status remain intact. Function source support is unchanged. Forced standalone
indirect eval still reports its missing bridge; ordinary deployed eval is fixed
under #1771 and produces the original `3` with clean exit/empty stderr.

Bounded regressions independently require process exit within 30 seconds and
pipe closure within five more seconds, supplying stdin EOF and draining both
streams concurrently. They cover both originals, ordinary unhandled throws and
deployed eval in both contexts. They never count forced kills as successful
completion. Node references were established before original SharpTS runs.
