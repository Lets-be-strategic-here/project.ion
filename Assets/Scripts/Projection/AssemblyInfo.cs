using System.Runtime.CompilerServices;

// Lets the tests build / inspect PhotoData contents directly (PhotoPiece / PhotoEntity are internal).
[assembly: InternalsVisibleTo("Ion.Tests.EditMode")]
[assembly: InternalsVisibleTo("Ion.Tests.PlayMode")]
