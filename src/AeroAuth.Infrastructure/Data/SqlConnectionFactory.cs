using System.Data.Common;
using AeroAuth.Core.Interfaces;
using Microsoft.Data.SqlClient;

namespace AeroAuth.Infrastructure.Data;

/// <summary>Creates a new, not yet opened SQL Server connection. The connection string is configuration, never hard-coded.</summary>
public sealed class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("The connection string is missing.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public DbConnection Create() => new SqlConnection(_connectionString);
}
