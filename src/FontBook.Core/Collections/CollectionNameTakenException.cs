namespace FontBook.Core.Collections;

/// <summary>Thrown when creating or renaming a collection to a name that already exists.</summary>
public sealed class CollectionNameTakenException : InvalidOperationException
{
    public CollectionNameTakenException()
    {
    }

    public CollectionNameTakenException(string name)
        : base($"A collection named \"{name}\" already exists.")
    {
        Name = name;
    }

    public CollectionNameTakenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? Name { get; }
}
