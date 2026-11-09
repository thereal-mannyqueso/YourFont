using System.Diagnostics.CodeAnalysis;

namespace FontBook.Core.Models;

/// <summary>A user-created collection of fonts (stored in SQLite).</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "\"Collection\" is the product's domain term, not a .NET collection type.")]
public sealed record FontCollection(long Id, string Name, DateTimeOffset CreatedAt);
