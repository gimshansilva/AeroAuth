using System.Data;
using AeroAuth.Core.Interfaces;
using Dapper;

namespace AeroAuth.Infrastructure.Repositories;

public class CounterRepository(IUnitOfWork uow) : BaseRepository(uow), ICounterRepository
{
    /// <summary>
    /// Calls dbo.usp_GetNextCounter. Because the connection and transaction come from the unit of work, the number is taken
    /// inside the caller's transaction: if the caller rolls back, the number goes back too and no gap is left.
    /// </summary>
    public async Task<int> NextAsync(int? organizationId, string counterKey, int year)
    {
        var p = new DynamicParameters();
        p.Add("@OrganizationId", organizationId, DbType.Int32);
        p.Add("@CounterKey", counterKey, DbType.String);
        p.Add("@CounterYear", year, DbType.Int32);
        p.Add("@NextValue", dbType: DbType.Int32, direction: ParameterDirection.Output);
        await Connection.ExecuteAsync(new CommandDefinition("dbo.usp_GetNextCounter", p, Transaction, commandType: CommandType.StoredProcedure));
        return p.Get<int>("@NextValue");
    }
}
