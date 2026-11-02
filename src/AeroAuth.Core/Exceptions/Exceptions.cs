namespace AeroAuth.Core.Exceptions;

/// <summary>The record does not exist, or it belongs to another organization (the caller cannot tell which).</summary>
public class NotFoundException(string message) : Exception(message);

/// <summary>The request conflicts with the current state, for example an item that was already reviewed.</summary>
public class ConflictException(string message) : Exception(message);

/// <summary>An attempt to read or write data of another organization, or an account without an organization.</summary>
public class TenantViolationException(string message) : Exception(message);

/// <summary>The request is not valid for a business reason.</summary>
public class ValidationFailedException(string message) : Exception(message);
