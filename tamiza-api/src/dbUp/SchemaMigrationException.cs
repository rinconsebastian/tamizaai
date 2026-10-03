namespace Tamiza.DbUp;

/// <summary>The metadata schema could not be brought up to date.</summary>
public sealed class SchemaMigrationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
