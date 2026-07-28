namespace PublicationSite.Api.Common.Exceptions;

public abstract class AppException(string message) : Exception(message);

public class NotFoundException(string entity, object key)
    : AppException($"{entity} with id '{key}' was not found.");

public class ForbiddenException(string message = "You do not have permission to perform this action.")
    : AppException(message);

public class ConflictException(string message) : AppException(message);

public class ValidationAppException(IReadOnlyList<string> errors)
    : AppException("One or more validation errors occurred.")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public class BusinessRuleException(string message) : AppException(message);
