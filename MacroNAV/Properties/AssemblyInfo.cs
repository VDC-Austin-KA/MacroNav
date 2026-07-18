using System.Runtime.CompilerServices;

// ClashCompat is internal on purpose — it is an implementation detail of the
// Navisworks version shim. The round-trip harness in tests\RoundTrip exercises
// it directly against a live document, so it needs this seam.
[assembly: InternalsVisibleTo("MacroNAVTests")]
