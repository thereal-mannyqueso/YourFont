// WPF projects omit System.IO from implicit usings (to avoid clashing with System.Windows.Shapes.Path).
// FontBook.App only needs IOException from it, which is safe to import globally.
global using System.IO;
