namespace AgentSandbox.Capabilities.SQL;

public interface ISqlCapability
{
    SqlQueryResult ExecuteSql(string statement, SqlQueryOptions? options = null);
}


