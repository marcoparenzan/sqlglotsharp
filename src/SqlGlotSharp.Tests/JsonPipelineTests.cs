namespace SqlGlotSharp.Tests;

public class JsonPipelineTests
{
    [Fact]
    public async Task Json_queries_match_Python_AST_and_production_Postgres_SQL()
    {
        await TypeCastPipelineTests.CompareSamples([
            "SELECT payload -> 'device' FROM measurements",
            "SELECT payload ->> 'device' FROM measurements",
            "SELECT payload #> '{device,id}' FROM measurements",
            "SELECT payload #>> '{device,id}' FROM measurements",
            "SELECT payload -> 'device' ->> 'id' FROM measurements",
            "SELECT payload -> 0 FROM measurements",
            "SELECT payload -> '0' FROM measurements",
            "SELECT payload -> -1 FROM measurements",
            "SELECT payload ->> 'value' AS v FROM measurements WHERE payload ->> 'name' ILIKE 'sensor%'",
            "SELECT CASE WHEN payload ->> 'name' IS NULL THEN 'unknown' ELSE payload ->> 'name' END FROM measurements",
            "SELECT (payload ->> 'value')::DECIMAL(10, 2) FROM measurements",
            "SELECT DISTINCT ON (payload ->> 'id') payload ->> 'id', ts FROM measurements ORDER BY payload ->> 'id', ts DESC",
            "SELECT payload #>> '{device,id}' FROM measurements WHERE ts > NOW() - INTERVAL '1 day'",
            "SELECT payload /* payload */ -> /* key */ 'device' /* result */ FROM measurements",
            "SELECT payload -> FROM measurements"
        ], ["postgres"]);
    }
}
