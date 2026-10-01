using System.Runtime.CompilerServices;

// Lets the EditMode tests build PhotoData contents directly (PhotoPiece / PhotoEntity are internal).
[assembly: InternalsVisibleTo("Ion.Tests.EditMode")]
