namespace RetailSuite.Infrastructure.Exceptions;

/// <summary>Thrown when a requested resource cannot be found. Maps to HTTP 404.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string resourceName, object key)
        : base($"{resourceName} with id '{key}' was not found.") { }
}

/// <summary>Thrown when a create/update would violate a uniqueness constraint. Maps to HTTP 409.</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>Thrown when a domain rule is violated (e.g. confirming an already-confirmed order). Maps to HTTP 422.</summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}
