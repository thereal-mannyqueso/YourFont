namespace FontBook.Core.Models;

/// <summary>Says that the font family with <paramref name="FontKey"/> belongs to a collection.</summary>
public sealed record CollectionMembership(long CollectionId, string FontKey);
